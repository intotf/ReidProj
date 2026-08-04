namespace ReidFeature.Payloads;

/// <summary>
/// 四维相似度分数 — 各维度独立相似度及加权总分
/// </summary>
public readonly record struct TrackSimilarityScores(
    float Cloth,
    float Head,
    float Body,
    float Gait)
{ 
    /// <summary>
    /// 按自定义权重计算加权总分
    /// </summary>
    public float ComputeTotal(float wCloth, float wHead, float wBody, float wGait)
    {
        return wCloth * Cloth + wHead * Head + wBody * Body + wGait * Gait;
    }
}
