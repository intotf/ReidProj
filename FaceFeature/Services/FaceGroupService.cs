using FaceFeature.Helpers;
using FaceFeature.Payloads;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;

namespace FaceFeature.Services;

/// <summary>
/// 人脸分组管理服务 — 基于文件存储：注册时把原始图片与特征索引（index.json）落盘，
/// 启动时仅读取各分组 index.json 载入内存（不扫描图片、不重新提取特征）。
/// 目录结构：datas/facegroups/{groupId}/images/{faceId}.jpg + datas/facegroups/{groupId}/index.json
/// </summary>
public sealed class FaceGroupService
{
    private const string IndexFileName = "index.json";
    private const string ImagesDirName = "images";

    private readonly DetectService _detectService;
    private readonly FaceQualityOptions _qualityOptions;
    private readonly ILogger<FaceGroupService> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, List<RegisteredFace>> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;

    /// <summary>
    /// 初始化人脸分组管理服务并读取已保存的特征索引
    /// </summary>
    public FaceGroupService(
        DetectService detectService,
        IOptions<FaceQualityOptions> qualityOptions,
        ILogger<FaceGroupService> logger)
    {
        _detectService = detectService;
        _qualityOptions = qualityOptions.Value;
        _logger = logger;
        _root = Path.Combine(AppContext.BaseDirectory, "datas", "facegroups");
        LoadIndexes();
    }

    /// <summary>
    /// 获取指定分组下全部人脸（含特征向量），用于 1:N 比对
    /// </summary>
    public Task<FacePerson[]> GetPersonsAsync(string groupId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_groups.TryGetValue(groupId, out var list) || list.Count == 0)
            {
                return Task.FromResult(Array.Empty<FacePerson>());
            }

            return Task.FromResult(
                list.Select(r => new FacePerson(r.Id, r.GroupId, r.Name, r.Features)).ToArray());
        }
    }

    /// <summary>
    /// 列出指定分组下所有人脸（不含特征向量）
    /// </summary>
    public Task<FaceInfo[]> ListAsync(string groupId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_groups.TryGetValue(groupId, out var list))
            {
                return Task.FromResult(Array.Empty<FaceInfo>());
            }

            return Task.FromResult(list.Select(r => r.ToInfo(includeFeatures: false)).ToArray());
        }
    }

    /// <summary>
    /// 查询单张已注册人脸
    /// </summary>
    public Task<FaceInfo?> GetAsync(string groupId, string faceId, bool includeFeatures, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_groups.TryGetValue(groupId, out var list))
            {
                return Task.FromResult<FaceInfo?>(null);
            }

            var record = list.FirstOrDefault(r => string.Equals(r.Id, faceId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(record?.ToInfo(includeFeatures));
        }
    }

    /// <summary>
    /// 注册人脸：解码图片 → 检测对齐 → 提取特征 → 保存图片与特征索引 → 加入内存注册表
    /// </summary>
    /// <param name="groupId">分组 ID</param>
    /// <param name="name">人物名称</param>
    /// <param name="imageStream">原始图片字节流</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<FaceRegistrationResult> RegisterAsync(
        string groupId,
        string name,
        Stream imageStream,
        CancellationToken cancellationToken)
    {
        groupId = ValidateSegment(groupId, nameof(groupId));
        name = ValidateSegment(name, nameof(name));

        using var imageBuffer = new MemoryStream();
        try
        {
            await imageStream.CopyToAsync(imageBuffer, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.FaceStoreError(_logger, "读取上传图片失败", ex);
            return FaceRegistrationResult.Failed("图片读取失败");
        }

        if (imageBuffer.Length == 0)
        {
            return FaceRegistrationResult.Failed("请求体为空");
        }

        Image<Rgb24> image;
        try
        {
            imageBuffer.Position = 0;
            image = Image.Load<Rgb24>(imageBuffer);
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.ImageDecodeFailed(_logger, ex);
            return FaceRegistrationResult.Failed("图片解码失败");
        }

        FaceDetection? detection;
        using (image)
        {
            detection = _detectService.DetectBestFace(image);
        }

        if (detection is null)
        {
            return FaceRegistrationResult.Failed("未检测到可用人脸");
        }

        var id = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}-{Guid.NewGuid():N}"[..28];
        var imagesDir = Path.Combine(_root, groupId, ImagesDirName);
        var filePath = Path.Combine(imagesDir, id + ".jpg");
        try
        {
            Directory.CreateDirectory(imagesDir);
            imageBuffer.Position = 0;
            await using var fileStream = File.Create(filePath);
            await imageBuffer.CopyToAsync(fileStream, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.FaceStoreError(_logger, "保存注册图片失败", ex);
            return FaceRegistrationResult.Failed("保存注册图片失败");
        }

        var record = new RegisteredFace
        {
            Id = id,
            GroupId = groupId,
            Name = name,
            Features = detection.Features,
            Confidence = detection.Confidence,
            Sharpness = detection.Sharpness,
            Bbox = detection.Bbox,
            ImagePath = filePath,
            RegisteredAt = DateTime.Now,
        };

        lock (_lock)
        {
            if (!_groups.TryGetValue(groupId, out var list))
            {
                list = new List<RegisteredFace>();
                _groups[groupId] = list;
            }
            list.Add(record);
        }

        try
        {
            SaveIndex(groupId);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                if (_groups.TryGetValue(groupId, out var list))
                {
                    list.Remove(record);
                }
            }
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // 回滚图片失败不阻塞错误上报
            }
            Log.FaceStoreError(_logger, "保存特征索引失败", ex);
            return FaceRegistrationResult.Failed("保存特征索引失败");
        }

        if (_qualityOptions.Enabled && record.Sharpness < _qualityOptions.SharpnessThreshold)
        {
            Log.FaceGalleryLowQuality(_logger, $"{groupId}/{name}/{id}", record.Sharpness);
        }

        return FaceRegistrationResult.Ok(record.ToInfo(includeFeatures: true));
    }

    /// <summary>
    /// 删除指定人脸：更新特征索引并删除磁盘图片（空目录一并清理）
    /// </summary>
    public Task<bool> DeleteAsync(string groupId, string faceId, CancellationToken cancellationToken)
    {
        groupId = ValidateSegment(groupId, nameof(groupId));
        faceId = ValidateSegment(faceId, nameof(faceId));

        RegisteredFace? record;
        lock (_lock)
        {
            if (!_groups.TryGetValue(groupId, out var list))
            {
                return Task.FromResult(false);
            }

            record = list.FirstOrDefault(r => string.Equals(r.Id, faceId, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                return Task.FromResult(false);
            }

            list.Remove(record);
        }

        try
        {
            SaveIndex(groupId);
        }
        catch (Exception ex)
        {
            // 索引写入失败则回滚内存记录，避免重启后数据不一致
            lock (_lock)
            {
                if (_groups.TryGetValue(groupId, out var list))
                {
                    list.Add(record);
                }
            }
            Log.FaceStoreError(_logger, $"删除注册人脸失败: {groupId}/{faceId}", ex);
            return Task.FromResult(false);
        }

        try
        {
            if (File.Exists(record.ImagePath))
            {
                File.Delete(record.ImagePath);
            }

            var imagesDir = Path.GetDirectoryName(record.ImagePath);
            if (imagesDir is not null && Directory.Exists(imagesDir) && !Directory.EnumerateFileSystemEntries(imagesDir).Any())
            {
                Directory.Delete(imagesDir);
            }

            var groupDir = Path.Combine(_root, groupId);
            if (Directory.Exists(groupDir) && !Directory.EnumerateFileSystemEntries(groupDir).Any())
            {
                Directory.Delete(groupDir);
            }
        }
        catch (Exception ex)
        {
            Log.FaceStoreError(_logger, $"删除注册图片失败: {groupId}/{faceId}", ex);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 启动时读取各分组 index.json 载入内存（不扫描图片目录、不重新提取特征）
    /// </summary>
    private void LoadIndexes()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var groupDir in Directory.GetDirectories(_root))
        {
            var groupId = Path.GetFileName(groupDir);
            var indexPath = Path.Combine(groupDir, IndexFileName);
            if (!File.Exists(indexPath))
            {
                continue;
            }

            try
            {
                var bytes = File.ReadAllBytes(indexPath);
                var stored = JsonSerializer.Deserialize(bytes, AppJsonSerializerContext.Default.PersistedFaceArray);
                var list = new List<RegisteredFace>();
                if (stored is not null)
                {
                    foreach (var s in stored)
                    {
                        if (string.IsNullOrEmpty(s.Id)
                            || s.FaceFeatures is null
                            || s.FaceFeatures.Length == 0
                            || string.IsNullOrEmpty(s.ImageFile)
                            || s.ImageFile.Contains("..")
                            || Path.IsPathRooted(s.ImageFile))
                        {
                            continue;
                        }

                        list.Add(new RegisteredFace
                        {
                            Id = s.Id,
                            GroupId = string.IsNullOrEmpty(s.GroupId) ? groupId : s.GroupId,
                            Name = s.Name ?? string.Empty,
                            Features = s.FaceFeatures,
                            Confidence = s.Confidence,
                            Sharpness = s.Sharpness,
        Bbox = new Rectangle(s.BboxX, s.BboxY, s.BboxWidth, s.BboxHeight),
                            ImagePath = Path.Combine(groupDir, s.ImageFile),
                            RegisteredAt = s.RegisteredAt,
                        });
                    }
                }
                _groups[groupId] = list;
            }
            catch (Exception ex)
            {
                Log.FaceStoreError(_logger, $"加载分组索引失败: {indexPath}", ex);
            }
        }
    }

    /// <summary>
    /// 把指定分组的内存记录原子写入 index.json
    /// </summary>
    private void SaveIndex(string groupId)
    {
        PersistedFace[] stored;
        lock (_lock)
        {
            stored = _groups.TryGetValue(groupId, out var list)
                ? list.Select(r => r.ToStored()).ToArray()
                : Array.Empty<PersistedFace>();
        }

        var groupDir = Path.Combine(_root, groupId);
        Directory.CreateDirectory(groupDir);
        var indexPath = Path.Combine(groupDir, IndexFileName);
        var tmpPath = indexPath + ".tmp";
        var json = JsonSerializer.SerializeToUtf8Bytes(stored, AppJsonSerializerContext.Default.PersistedFaceArray);
        File.WriteAllBytes(tmpPath, json);
        File.Move(tmpPath, indexPath, overwrite: true);
    }

    /// <summary>
    /// 校验路径段（分组 / 名称 / 人脸 ID），拒绝空值、路径分隔符与非法文件名字符
    /// </summary>
    private static string ValidateSegment(string value, string paramName)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length == 0
            || value == "."
            || value == ".."
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.IndexOf('/') >= 0
            || value.IndexOf('\\') >= 0)
        {
            throw new ArgumentException($"{paramName} 包含非法字符");
        }
        return value;
    }

    /// <summary>
    /// 内存中的已注册人脸记录（含特征与元数据）
    /// </summary>
    private sealed class RegisteredFace
    {
        public required string Id { get; init; }
        public required string GroupId { get; init; }
        public required string Name { get; init; }
        public required float[] Features { get; init; }
        public required float Confidence { get; init; }
        public required float Sharpness { get; init; }
        public required Rectangle Bbox { get; init; }
        public required string ImagePath { get; init; }
        public required DateTime RegisteredAt { get; init; }

        public FaceInfo ToInfo(bool includeFeatures) =>
            new(Id, GroupId, Name, Confidence, Sharpness, Bbox, RegisteredAt,
                includeFeatures ? Features : null);

        public PersistedFace ToStored() => new()
        {
            Id = Id,
            GroupId = GroupId,
            Name = Name,
            Confidence = Confidence,
            Sharpness = Sharpness,
            BboxX = Bbox.X,
            BboxY = Bbox.Y,
            BboxWidth = Bbox.Width,
            BboxHeight = Bbox.Height,
            RegisteredAt = RegisteredAt,
            FaceFeatures = Features,
            ImageFile = $"{ImagesDirName}/{Path.GetFileName(ImagePath)}",
        };
    }
}
