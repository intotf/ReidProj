using SixLabors.ImageSharp;

namespace FaceFeature.Payloads;

/// <summary>
/// 视频多帧融合结果 — 融合后的特征向量与代表帧元数据
/// </summary>
/// <param name="Features">融合后的 512 维特征向量</param>
/// <param name="Bbox">代表帧（置信度最高帧）的人脸边界框（内部统一使用 Rectangle）</param>
/// <param name="Confidence">代表帧的人脸置信度</param>
/// <param name="Sharpness">代表帧的清晰度</param>
/// <param name="FrameCount">参与融合的帧数</param>
/// <param name="Early">是否因融合收敛而提前完成</param>
internal sealed record FusedFaceResult(
    float[] Features,
    Rectangle Bbox,
    float Confidence,
    float Sharpness,
    int FrameCount,
    bool Early);
