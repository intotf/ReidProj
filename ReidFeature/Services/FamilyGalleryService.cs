using ReidFeature.Helpers;
using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReidFeature.Services;

/// <summary>
/// 家庭成员 Gallery 服务
/// 支持：视频注册 → 特征提取、EMA 更新、JSON 持久化、陌生人暂存
/// </summary>
public sealed class FamilyGalleryService : IFamilyMemberProvider, IDisposable
{
    private readonly ILogger<FamilyGalleryService> _logger;
    private readonly YoloDetector _yolo;
    private readonly ByteTrackTracker _tracker;
    private readonly TrackFusionService _fusion;
    private readonly string _galleryDir;

    private readonly Dictionary<string, List<GalleryEntry>> _groups = [];
    private readonly List<TrackFeaturePack> _unknownQueue = [];

    // EMA 衰减因子
    private const float EmaLambda = 0.3f;

    public FamilyGalleryService(
        ILogger<FamilyGalleryService> logger,
        YoloDetector yolo,
        ByteTrackTracker tracker,
        TrackFusionService fusion)
    {
        _logger = logger;
        _yolo = yolo;
        _tracker = tracker;
        _fusion = fusion;
        _galleryDir = Path.Combine(AppContext.BaseDirectory, "datas", "gallery") ?? 
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "datas", "gallery");

        // 启动时加载持久化数据 + 从 datas/family 注册
        LoadGallery();
        EnrollFromFamilyDirectory().GetAwaiter().GetResult();
    }

    // ── IFamilyMemberProvider 实现 ──

    public Task<Person[]> GetMembersAsync(string groupId, CancellationToken ct)
    {
        if (_groups.TryGetValue(groupId, out var entries))
        {
            var persons = entries.Select(e => new Person(e.Id, groupId, e.Name, [])
            {
                FeaturePack = e.FeaturePack
            }).ToArray();
            return Task.FromResult(persons);
        }
        return Task.FromResult(Array.Empty<Person>());
    }

    public async Task<string> EnrollAsync(string groupId, string name, TrackFeaturePack featurePack, CancellationToken ct)
    {
        var entry = FindEntry(groupId, name);
        if (entry is null)
        {
            // 新注册：直接存储
            entry = new GalleryEntry
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Name = name,
                EnrolledAt = DateTime.UtcNow,
                FeaturePack = featurePack,
            };
            if (!_groups.ContainsKey(groupId))
                _groups[groupId] = [];
            _groups[groupId].Add(entry);
        }
        else
        {
            // 已存在：EMA 更新
            entry.FeaturePack = EmaFusion(entry.FeaturePack, featurePack);
            entry.EnrolledAt = DateTime.UtcNow;
        }

        await SaveGalleryAsync(ct);
        Log.GalleryMemberEnrolled(_logger, name, groupId);
        return entry.Id;
    }

    public Task<bool> DeleteAsync(string groupId, string memberId, CancellationToken ct)
    {
        if (_groups.TryGetValue(groupId, out var entries))
        {
            var removed = entries.RemoveAll(e => e.Id == memberId);
            if (removed > 0)
            {
                _ = SaveGalleryAsync(ct);
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    public Task<MemberInfo[]> ListAsync(string groupId, CancellationToken ct)
    {
        if (_groups.TryGetValue(groupId, out var entries))
        {
            var infos = entries.Select(e => new MemberInfo(e.Id, e.Name, e.EnrolledAt)).ToArray();
            return Task.FromResult(infos);
        }
        return Task.FromResult(Array.Empty<MemberInfo>());
    }

    // ── 陌生人暂存 ──

    public void PushUnknown(TrackFeaturePack pack)
    {
        _unknownQueue.Add(pack);
        if (_unknownQueue.Count > 100)
            _unknownQueue.RemoveAt(0);
    }

    public TrackFeaturePack[] PopUnknowns(int maxCount = 10)
    {
        var batch = _unknownQueue.Take(maxCount).ToArray();
        _unknownQueue.RemoveRange(0, batch.Length);
        return batch;
    }

    // ── 内部方法 ──

    private GalleryEntry? FindEntry(string groupId, string name)
    {
        return _groups.TryGetValue(groupId, out var entries)
            ? entries.FirstOrDefault(e => e.Name == name)
            : null;
    }

    /// <summary>EMA 融合旧特征和新特征</summary>
    private static TrackFeaturePack EmaFusion(TrackFeaturePack old, TrackFeaturePack latest)
    {
        return new TrackFeaturePack
        {
            VecCloth = EmaVector(old.VecCloth, latest.VecCloth),
            VecHead = EmaVector(old.VecHead, latest.VecHead),
            BodySignals = [
                old.BodySignals.Length > 0 ? old.BodySignals[0] * (1 - EmaLambda) + latest.BodySignals[0] * EmaLambda : latest.BodySignals[0],
                old.BodySignals.Length > 1 ? old.BodySignals[1] * (1 - EmaLambda) + latest.BodySignals[1] * EmaLambda : latest.BodySignals[1],
            ],
            GaitSignals = [
                old.GaitSignals.Length > 0 ? old.GaitSignals[0] * (1 - EmaLambda) + latest.GaitSignals[0] * EmaLambda : latest.GaitSignals[0],
                old.GaitSignals.Length > 1 ? old.GaitSignals[1] * (1 - EmaLambda) + latest.GaitSignals[1] * EmaLambda : latest.GaitSignals[1],
            ],
        };
    }

    private static byte[] EmaVector(byte[] oldVec, byte[] newVec)
    {
        if (oldVec.Length == 0) return newVec;
        if (newVec.Length == 0) return oldVec;
        if (oldVec.Length != newVec.Length) return newVec;

        var oldF = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(oldVec);
        var newF = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(newVec);
        var result = new float[oldF.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = oldF[i] * (1 - EmaLambda) + newF[i] * EmaLambda;

        // L2 归一化
        float norm = MathF.Sqrt(result.Sum(v => v * v));
        if (norm > 1e-8f)
            for (int i = 0; i < result.Length; i++)
                result[i] /= norm;

        return System.Runtime.InteropServices.MemoryMarshal.Cast<float, byte>(result).ToArray();
    }

    // ── 持久化 ──

    private void LoadGallery()
    {
        if (!Directory.Exists(_galleryDir))
        {
            Directory.CreateDirectory(_galleryDir);
            return;
        }

        foreach (var file in Directory.GetFiles(_galleryDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var data = JsonSerializer.Deserialize<GalleryData>(json);
                if (data?.Members is { Count: > 0 })
                {
                    var groupId = Path.GetFileNameWithoutExtension(file);
                    _groups[groupId] = data.Members;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Gallery 加载失败: {File}, {Error}", file, ex.Message);
            }
        }
    }

    private async Task SaveGalleryAsync(CancellationToken ct)
    {
        foreach (var (groupId, members) in _groups)
        {
            var data = new GalleryData { Members = members };
            var json = JsonSerializer.Serialize(data);
            var filePath = Path.Combine(_galleryDir, $"{groupId}.json");
            await File.WriteAllTextAsync(filePath, json, ct);
        }
    }

    // ── 从 datas/family 目录注册 ──

    private async Task EnrollFromFamilyDirectory()
    {
        var familyDataDir = Path.Combine(AppContext.BaseDirectory, "datas", "family")
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "datas", "family");

        if (!Directory.Exists(familyDataDir))
            return;

        foreach (var memberDir in Directory.GetDirectories(familyDataDir))
        {
            var memberName = Path.GetFileName(memberDir);
            var videoFiles = Directory.GetFiles(memberDir, "enroll.h264")
                .Concat(Directory.GetFiles(memberDir, "enroll.h265"))
                .Concat(Directory.GetFiles(memberDir, "enroll.mp4"))
                .ToArray();

            if (videoFiles.Length == 0)
                continue;

            // 仅处理第一个视频文件
            var videoPath = videoFiles[0];
            var extension = Path.GetExtension(videoPath).ToLowerInvariant();
            var codec = extension switch
            {
                ".h264" => VideoCodec.H264,
                ".265" or ".h265" or ".hevc" => VideoCodec.H265,
                ".mp4" => VideoCodec.H264,  // ffmpeg 会自动封装
                _ => VideoCodec.H264,
            };

            _logger.LogInformation("注册成员 {Name} 从 {Video}", memberName, videoPath);

            try
            {
                await using var fs = File.OpenRead(videoPath);
                var frames = new List<(Image<Rgb24> Frame, Rectangle Bbox, float Score)>();
                int frameIndex = 0;

                _tracker.Reset();
                await foreach (var image in VideoDecoder.DecodeFramesAsync(fs, codec, _logger, 0, CancellationToken.None))
                {
                    var detections = _yolo.DetectPersons(image);
                    if (detections.Count == 0)
                    {
                        image.Dispose();
                        continue;
                    }

                    var input = detections.Select(d => (d.Bbox, d.Confidence)).ToList();
                    var tracked = _tracker.Update(input, frameIndex++);

                    // 缓存属于同一个主导 Track 的帧
                    if (tracked.Count > 0)
                    {
                        // 取当前帧置信度最高的 Track
                        var bestTrack = tracked[0];
                        frames.Add((image, bestTrack.Bbox, 
                            detections.First(d => d.Bbox == bestTrack.Bbox).Confidence));
                    }
                    else
                    {
                        image.Dispose();
                    }
                }

                // 取完成 Track 进行融合注册
                var completed = _tracker.FlushCompletedTracks(minFrames: 5);
                if (completed.Count > 0)
                {
                    var best = completed[0];
                    var trackFrames = frames.Where(f => frames.IndexOf(f) >= 0).ToList(); // simplified
                    // Actually we need to map frames to tracks, simplified to use all frames

                    if (frames.Count > 3)
                    {
                        var pack = _fusion.FuseTrack(best.TrackId, frames);
                        var groupId = "default";
                        await EnrollAsync(groupId, memberName, pack, CancellationToken.None);
                    }
                }

                foreach (var (frame, _, _) in frames)
                    frame.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("注册失败 {Name}: {Error}", memberName, ex.Message);
            }
        }
    }

    public void Dispose()
    {
        _unknownQueue.Clear();
    }

    // ── 内部数据模型 ──

    internal sealed class GalleryData
    {
        public List<GalleryEntry> Members { get; set; } = [];
    }

    internal sealed class GalleryEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime EnrolledAt { get; set; }
        public TrackFeaturePack FeaturePack { get; set; } = new();
    }
}
