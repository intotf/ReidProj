using ReidFeature.Helpers;
using ReidFeature.Payloads;
using System.Buffers;
using System.Numerics.Tensors;
using System.Text.Json;

namespace ReidFeature.Services;

/// <summary>
/// 家庭成员 Gallery 服务
/// 支持：视频注册 → 特征提取、EMA 更新、JSON 持久化、陌生人暂存
/// </summary>
public sealed class FamilyGalleryService : IFamilyMemberProvider, IDisposable
{
    private readonly ILogger<FamilyGalleryService> _logger;
    private readonly string _galleryDir;

    private readonly Dictionary<string, List<GalleryEntry>> _groups = [];
    private readonly List<TrackFeaturePack> _unknownQueue = [];

    // Gallery 状态并发保护：_groups / _unknownQueue 的读写均在锁内进行
    private readonly object _syncRoot = new();

    // EMA 衰减因子
    private const float EmaLambda = 0.3f;

    /// <summary>
    /// 初始化家庭成员 Gallery 服务，并加载持久化数据
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public FamilyGalleryService(ILogger<FamilyGalleryService> logger)
    {
        _logger = logger;
        _galleryDir = Path.Combine(AppContext.BaseDirectory, "datas", "gallery") ??
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "datas", "gallery");

        // 启动时加载持久化数据
        LoadGallery();
    }

    // ── IFamilyMemberProvider 实现 ──

    /// <inheritdoc />
    public Task<Person[]> GetMembersAsync(string groupId, CancellationToken ct)
    {
        lock (_syncRoot)
        {
            if (_groups.TryGetValue(groupId, out var entries))
            {
                var persons = entries.Select(e => new Person(e.Id, groupId, e.Name)
                {
                    FeaturePack = e.FeaturePack
                }).ToArray();
                return Task.FromResult(persons);
            }
            return Task.FromResult(Array.Empty<Person>());
        }
    }

    /// <inheritdoc />
    public async Task<string> EnrollAsync(string groupId, string name, TrackFeaturePack featurePack, CancellationToken ct)
    {
        string entryId;
        lock (_syncRoot)
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
                {
                    _groups[groupId] = [];
                }
                _groups[groupId].Add(entry);
            }
            else
            {
                // 已存在：EMA 更新
                entry.FeaturePack = EmaFusion(entry.FeaturePack, featurePack);
                entry.EnrolledAt = DateTime.UtcNow;
            }
            entryId = entry.Id;
        }

        // 锁外执行 IO（async 不能跨 await 持锁）
        await SaveGalleryAsync(ct);
        Log.GalleryMemberEnrolled(_logger, name, groupId);
        return entryId;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string groupId, string memberId, CancellationToken ct)
    {
        lock (_syncRoot)
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
    }

    /// <inheritdoc />
    public Task<MemberInfo[]> ListAsync(string groupId, CancellationToken ct)
    {
        lock (_syncRoot)
        {
            if (_groups.TryGetValue(groupId, out var entries))
            {
                var infos = entries.Select(e => new MemberInfo(e.Id, e.Name, e.EnrolledAt)).ToArray();
                return Task.FromResult(infos);
            }
            return Task.FromResult(Array.Empty<MemberInfo>());
        }
    }

    // ── 陌生人暂存 ──

    /// <summary>
    /// 暂存一个未识别人员的特征包（最多保留 100 个，超出则移除最旧的）
    /// </summary>
    /// <param name="pack">未识别人员的四维特征包</param>
    public void PushUnknown(TrackFeaturePack pack)
    {
        lock (_syncRoot)
        {
            _unknownQueue.Add(pack);
            if (_unknownQueue.Count > 100)
            {
                _unknownQueue.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 取出并移除指定数量的暂存未识别特征包
    /// </summary>
    /// <param name="maxCount">最多取出的数量（默认 10）</param>
    /// <returns>暂存的特征包数组</returns>
    public TrackFeaturePack[] PopUnknowns(int maxCount = 10)
    {
        lock (_syncRoot)
        {
            var batch = _unknownQueue.Take(maxCount).ToArray();
            _unknownQueue.RemoveRange(0, batch.Length);
            return batch;
        }
    }

    // ── 内部方法 ──

    private GalleryEntry? FindEntry(string groupId, string name)
    {
        lock (_syncRoot)
        {
            return _groups.TryGetValue(groupId, out var entries)
                ? entries.FirstOrDefault(e => e.Name == name)
                : null;
        }
    }

    /// <summary>EMA 融合旧特征和新特征</summary>
    private static TrackFeaturePack EmaFusion(TrackFeaturePack old, TrackFeaturePack latest)
    {
        return new TrackFeaturePack
        {
            VecCloth = EmaVector(old.VecCloth, latest.VecCloth),
            VecHead = EmaVector(old.VecHead, latest.VecHead),
            BodySignals = [EmaDim(old.BodySignals, latest.BodySignals, 0), EmaDim(old.BodySignals, latest.BodySignals, 1)],
            GaitSignals = [EmaDim(old.GaitSignals, latest.GaitSignals, 0), EmaDim(old.GaitSignals, latest.GaitSignals, 1)],
        };
    }

    /// <summary>EMA 融合单个标量维度；任一侧该维度缺失（空数组）时取另一侧，避免 IndexOutOfRange</summary>
    private static float EmaDim(float[] oldArr, float[] newArr, int index)
    {
        if (newArr.Length <= index)
        {
            return oldArr.Length > index ? oldArr[index] : 0f;
        }
        if (oldArr.Length <= index)
        {
            return newArr[index];
        }
        return oldArr[index] * (1 - EmaLambda) + newArr[index] * EmaLambda;
    }

    private static float[] EmaVector(float[] oldVec, float[] newVec)
    {
        if (oldVec.Length == 0)
        {
            return newVec;
        }
        if (newVec.Length == 0)
        {
            return oldVec;
        }
        if (oldVec.Length != newVec.Length)
        {
            return newVec;
        }

        int floatDim = oldVec.Length;
        float[] poolBuffer = ArrayPool<float>.Shared.Rent(floatDim);
        try
        {
            Span<float> result = poolBuffer.AsSpan(0, floatDim);

            // result = oldVec * (1-λ) + newVec * λ（MultiplyAdd 单趟融合）
            TensorPrimitives.Multiply(oldVec, 1 - EmaLambda, result);
            TensorPrimitives.MultiplyAdd(newVec, EmaLambda, result, result);

            // L2 归一化
            float norm = TensorPrimitives.Norm(result);
            if (norm > 1e-8f)
            {
                TensorPrimitives.Divide(result, norm, result);
            }

            return result.ToArray();
        }
        finally
        {
            ArrayPool<float>.Shared.Return(poolBuffer);
        }
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
                var data = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.GalleryData);
                if (data?.Members is { Count: > 0 })
                {
                    var groupId = Path.GetFileNameWithoutExtension(file);
                    _groups[groupId] = data.Members;
                }
            }
            catch (Exception ex)
            {
                Log.GalleryLoadFailed(_logger, file, ex.Message);
            }
        }
    }

    private async Task SaveGalleryAsync(CancellationToken ct)
    {
        // 锁内快照，锁外写文件：避免遍历期间集合被并发修改（快照为浅拷贝，仅保证集合结构稳定）
        KeyValuePair<string, List<GalleryEntry>>[] snapshot;
        lock (_syncRoot)
        {
            snapshot = _groups
                .Select(kv => new KeyValuePair<string, List<GalleryEntry>>(kv.Key, [.. kv.Value]))
                .ToArray();
        }

        foreach (var (groupId, members) in snapshot)
        {
            var data = new GalleryData { Members = members };
            var json = JsonSerializer.Serialize(data, AppJsonSerializerContext.Default.GalleryData);
            var filePath = Path.Combine(_galleryDir, $"{groupId}.json");
            await File.WriteAllTextAsync(filePath, json, ct);
        }
    }

    /// <summary>
    /// 释放资源并清空暂存队列
    /// </summary>
    public void Dispose()
    {
        _unknownQueue.Clear();
    }
}
