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

    /// <summary>人脸特征模型文件名（models 目录下），默认 glintr100.onnx（ArcFace R100），可切换 w600k_r50.onnx 等</summary>
    public string FaceRecognitionModelName { get; set; } = "glintr100.onnx";

    /// <summary>清晰帧筛选配置</summary>
    public FaceQualityOptions FaceQuality { get; set; } = new();

    /// <summary>视频多帧融合配置</summary>
    public FaceFusionOptions Fusion { get; set; } = new();
}
