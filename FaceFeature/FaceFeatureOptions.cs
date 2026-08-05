namespace FaceFeature;

/// <summary>
/// 人脸流水线统一配置 — 检测、特征提取、清晰度筛选与视频融合共享的选项，
/// 从 appsettings.json 的 FaceFeature 节绑定
/// </summary>
public sealed class FaceFeatureOptions
{
    /// <summary>
    /// 人脸最小尺寸（像素，取宽高较小值）；低于该值的检测框直接丢弃，
    /// 避免小脸特征向量不可靠（门铃摄像头实测建议 80 以上）
    /// </summary>
    public int MinFaceSize { get; set; } = 80;

    /// <summary>
    /// 人脸检测置信度阈值（0~1）；低于该值的候选框丢弃。
    /// 小脸场景可适当下调（如 0.4~0.5）以提升召回，配合融合共识门控抑制误检
    /// </summary>
    public float ConfidenceThreshold { get; set; } = 0.6f;

    /// <summary>清晰帧筛选配置</summary>
    public FaceQualityOptions FaceQuality { get; set; } = new();

    /// <summary>视频多帧融合配置</summary>
    public FaceFusionOptions Fusion { get; set; } = new();
}
