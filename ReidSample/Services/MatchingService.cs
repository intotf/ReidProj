using Microsoft.EntityFrameworkCore;
using ReIdSample.Data;
using ReIdSample.Models.Dtos;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;

namespace ReIdSample.Services;

public class MatchingService
{
    private readonly ReidFeatureClient _reidClient;
    private readonly AppDbContext _db;
    private readonly ILogger<MatchingService> _logger;

    public MatchingService(ReidFeatureClient reidClient, AppDbContext db, ILogger<MatchingService> logger)
    {
        _reidClient = reidClient;
        _db = db;
        _logger = logger;
    }

    public async Task<MatchResponse> MatchAsync(Stream imageStream, float threshold, CancellationToken ct = default)
    {
        // 1. 调用 ReidFeature 检测人物 + 提取特征
        var detections = await _reidClient.HandleImageAsync(imageStream, ct: ct);

        if (detections.Count == 0)
        {
            return new MatchResponse
            {
                Detections = [],
                Threshold = threshold
            };
        }

        // 2. 加载所有注册照片特征到内存
        var registeredPhotos = await _db.FamilyMemberPhotos
            .Include(p => p.FamilyMember)
            .ToListAsync(ct);

        // 3. 对每个检测到的人物计算匹配
        var results = new List<DetectionResult>();
        foreach (var det in detections)
        {
            var matches = new List<PersonMatch>();
            foreach (var photo in registeredPhotos)
            {
                var similarity = CosineSimilarity(det.Features, photo.FeatureVector);

                matches.Add(new PersonMatch
                {
                    FamilyMemberId = photo.FamilyMemberId,
                    FamilyMemberName = photo.FamilyMember.Name,
                    PhotoId = photo.Id,
                    Similarity = similarity,
                    Matched = similarity >= threshold
                });
            }

            // 按相似度降序排列
            matches = matches.OrderByDescending(m => m.Similarity).ToList();

            results.Add(new DetectionResult
            {
                Bbox = new BoundingRect
                {
                    X = det.Bbox.X,
                    Y = det.Bbox.Y,
                    Width = det.Bbox.Width,
                    Height = det.Bbox.Height
                },
                Confidence = det.Confidence,
                Matches = matches
            });
        }

        _logger.LogInformation("匹配完成: {DetCount} 个检测目标, {RegCount} 个注册特征",
            detections.Count, registeredPhotos.Count);

        return new MatchResponse
        {
            Detections = results,
            Threshold = threshold
        };
    }

    /// <summary>
    /// 计算余弦相似度（TensorPrimitives SIMD 加速）
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<byte> featureA, ReadOnlySpan<byte> featureB)
    {
        var vectorA = MemoryMarshal.Cast<byte, float>(featureA);
        var vectorB = MemoryMarshal.Cast<byte, float>(featureB);
        return CosineSimilarity(vectorA, vectorB);
    }

    /// <summary>
    /// 计算余弦相似度（TensorPrimitives SIMD 加速）
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException("特征向量维度不匹配");
        }

        var dot = TensorPrimitives.Dot(vectorA, vectorB);
        var normA = TensorPrimitives.Norm(vectorA);
        var normB = TensorPrimitives.Norm(vectorB);

        return normA == 0 || normB == 0 ? 0 : dot / (normA * normB);
    }
}
