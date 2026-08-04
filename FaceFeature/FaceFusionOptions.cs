namespace FaceFeature;

/// <summary>
/// 视频多帧融合配置 — 质量加权、共识门控与收敛早停参数，
/// 从 appsettings.json 的 FaceFeature:Fusion 节绑定
/// </summary>
public sealed class FaceFusionOptions
{
    /// <summary>
    /// 融合至少需要积累的帧数，避免样本不足时提前收敛；
    /// 只有参与融合的帧数达到该值后，连续稳定判断才会生效
    /// </summary>
    public int MinFrames { get; set; } = 6;

    /// <summary>相邻融合向量余弦达到该值时视为一次“稳定”</summary>
    public float StabilityCosine { get; set; } = 0.99f;

    /// <summary>连续稳定次数达到该值时提前完成融合</summary>
    public int StableRequired { get; set; } = 2;

    /// <summary>
    /// 共识门控余弦阈值：新帧特征与当前融合向量的余弦低于该值时视为离群帧，
    /// 不参与融合（用于剔除错检、遮挡或混入的其他人脸）
    /// </summary>
    public float ConsensusGate { get; set; } = 0.85f;

    /// <summary>
    /// 共识门控预热帧数：前 N 帧无条件参与融合，先积累可靠的初始估计再开启门控，
    /// 避免早期估计偏差导致误拒正常帧
    /// </summary>
    public int ConsensusWarmup { get; set; } = 3;
}
