using System.Numerics.Tensors;
using System.Text.Json.Serialization;

namespace ReidFeature.Payloads;

/// <summary>
/// 四维特征包 — 对一个 Track 内多帧质量加权融合后的最终特征
/// 维度: 全身 ReID + 头肩 ReID + 体型标量 + 步态标量
/// </summary>
public sealed class TrackFeaturePack
{
    /// <summary>全身 ReID 权重</summary>
    public const float WCloth = 0.30f;
    /// <summary>头肩 ReID 权重</summary>
    public const float WHead = 0.40f;
    /// <summary>体型标量权重</summary>
    public const float WBody = 0.20f;
    /// <summary>步态标量权重</summary>
    public const float WGait = 0.10f;

    /// <summary>全身 ReID 特征向量（ResNet50-IBN-a 2048-d L2 归一化，JSON 序列化为 base64）</summary>
    [JsonConverter(typeof(FloatArrayBase64Converter))]
    public float[] VecCloth { get; set; } = [];

    /// <summary>头肩区域 ReID 特征向量（同一模型，仅裁剪区域不同，JSON 序列化为 base64）</summary>
    [JsonConverter(typeof(FloatArrayBase64Converter))]
    public float[] VecHead { get; set; } = [];

    /// <summary>体型标量 [0]=头身比, [1]=肩髋比（来自 MoveNet 关键点，换衣不变）</summary>
    public float[] BodySignals { get; set; } = [0f, 0f];

    /// <summary>步态标量 [0]=步频(Hz), [1]=水平摆幅(px)（来自 ByteTrack 轨迹）</summary>
    public float[] GaitSignals { get; set; } = [0f, 0f];

    /// <summary>
    /// 四维余弦融合 — 按权重加权计算两个特征包的相似度
    /// 权重: 全身 ReID 0.30 + 头肩 ReID 0.40 + 体型标量 0.20 + 步态标量 0.10
    /// </summary>
    /// <param name="a">特征包 A</param>
    /// <param name="b">特征包 B</param>
    /// <param name="wCloth">全身 ReID 权重</param>
    /// <param name="wHead">头肩 ReID 权重</param>
    /// <param name="wBody">体型标量权重</param>
    /// <param name="wGait">步态标量权重</param>
    /// <returns>加权融合相似度 [0, 1]</returns>
    public static float WeightedCosineSimilarity(
        TrackFeaturePack a,
        TrackFeaturePack b,
        float wCloth = WCloth,
        float wHead = WHead,
        float wBody = WBody,
        float wGait = WGait)
    {
        return ComputeScores(a, b).ComputeTotal(wCloth, wHead, wBody, wGait);
    }

    /// <summary>
    /// 计算两个特征包各维度的独立相似度及加权总分
    /// </summary>
    /// <param name="a">特征包 A</param>
    /// <param name="b">特征包 B</param>
    /// <returns>四维相似度分数</returns>
    public static TrackSimilarityScores ComputeScores(TrackFeaturePack a, TrackFeaturePack b)
    {
        return new TrackSimilarityScores(
            CosineSimilarity(a.VecCloth, b.VecCloth),
            CosineSimilarity(a.VecHead, b.VecHead),
            VectorSimilarity(a.BodySignals, b.BodySignals),
            VectorSimilarity(a.GaitSignals, b.GaitSignals));
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            return 0f;
        }

        float dot = TensorPrimitives.Dot(a, b);
        float normA = TensorPrimitives.Norm(a);
        float normB = TensorPrimitives.Norm(b);

        return normA == 0 || normB == 0 ? 0f : dot / (normA * normB);
    }

    /// <summary>
    /// 标量向量相似度 — 对短向量使用逆欧氏距离归一化
    /// </summary>
    private static float VectorSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0f;
        }

        // 任一向量为零向量（如关键点缺失导致的 [0,0]）时无法衡量相似度，
        // 返回 0 而非 1，避免该维度被虚高为满分
        float normA = TensorPrimitives.Norm(a);
        float normB = TensorPrimitives.Norm(b);
        if (normA == 0f || normB == 0f)
        {
            return 0f;
        }

        // 逆距离映射到 [0, 1]，dist=0 → 1.0, dist=1 → 0.5, dist→∞ → 0
        return 1f / (1f + TensorPrimitives.Distance(a, b));
    }
}
