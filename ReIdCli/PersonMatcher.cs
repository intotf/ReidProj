using System.Numerics.Tensors;
using System.Runtime.InteropServices;

namespace ReIdCli;

/// <summary>
/// 内存中的人物特征比对服务
/// </summary>
public class PersonMatcher
{
    private readonly float _threshold;

    public PersonMatcher(float threshold)
    {
        _threshold = threshold;
    }

    /// <summary>
    /// 在帧检测结果中寻找与目标人物匹配度最高的结果。
    /// 达到阈值则返回匹配结果，否则返回 null。
    /// </summary>
    public MatchResult? FindBestMatch(
        List<PersonDetection> frameDetections,
        List<TargetPerson> targets,
        string videoPath,
        string framePath)
    {
        float bestSimilarity = 0;
        string? bestTargetName = null;

        foreach (var detection in frameDetections)
        {
            var detVector = AsFloatSpan(detection.Features);

            foreach (var target in targets)
            {
                var targetVector = AsFloatSpan(target.Features);
                var similarity = CosineSimilarity(detVector, targetVector);

                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestTargetName = target.Name;
                }
            }
        }

        if (bestSimilarity >= _threshold && bestTargetName != null)
        {
            return new MatchResult
            {
                TargetName = bestTargetName,
                VideoPath = videoPath,
                FramePath = framePath,
                Similarity = bestSimilarity
            };
        }

        return null;
    }

    /// <summary>
    /// byte[] 零拷贝转为 float span（与 ReidSample 一致的方式）
    /// </summary>
    private static ReadOnlySpan<float> AsFloatSpan(byte[] bytes)
    {
        return MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
    }

    /// <summary>
    /// 余弦相似度（TensorPrimitives SIMD 加速）
    /// </summary>
    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        var dot = TensorPrimitives.Dot(a, b);
        var normA = TensorPrimitives.Norm(a);
        var normB = TensorPrimitives.Norm(b);

        return normA == 0 || normB == 0 ? 0 : dot / (normA * normB);
    }
}
