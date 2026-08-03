namespace FaceFeature;

/// <summary>
/// 人脸清晰度筛选配置，绑定 appsettings.json 的 FaceQuality 节
/// </summary>
public sealed class FaceQualityOptions
{
    /// <summary>是否启用清晰度筛选（视频流跳过模糊帧）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 清晰度阈值（对齐后 112×112 人脸的 Laplacian 方差），低于该值的帧视为模糊并跳过；
    /// 需按实际摄像头画质标定
    /// </summary>
    public float SharpnessThreshold { get; set; } = 100f;
}
