using Microsoft.EntityFrameworkCore;
using ReIdSample.Data;
using ReIdSample.Models;
using ReIdSample.Models.Dtos;

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

    public async Task<MatchResponse> MatchAsync(byte[] imageBytes, float threshold, CancellationToken ct = default)
    {
        // 1. 调用 ReidFeature 检测人物 + 提取特征
        var detections = await _reidClient.DetectAsync(imageBytes, ct);

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
            var queryFeatures = BytesToFloats(det.Features);
            var matches = new List<PersonMatch>();

            foreach (var photo in registeredPhotos)
            {
                var registeredFeatures = BytesToFloats(photo.FeatureVector);
                var similarity = CosineSimilarity(queryFeatures, registeredFeatures);

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
    /// 将 byte[] 还原为 float[]（4 字节一组）
    /// </summary>
    private static float[] BytesToFloats(byte[] bytes)
    {
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    /// <summary>
    /// 计算余弦相似度
    /// </summary>
    public static float CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
            throw new ArgumentException("特征向量维度不匹配");

        double dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            normA += vectorA[i] * vectorA[i];
            normB += vectorB[i] * vectorB[i];
        }

        if (normA == 0 || normB == 0) return 0;
        return (float)(dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}
