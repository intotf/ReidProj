namespace FaceFeature.Payloads;

/// <summary>
/// 人脸检测结果 — 坐标相对于原始输入图像，含人脸特征向量
/// </summary>
/// <param name="Bbox">人脸边界框</param>
/// <param name="Confidence">人脸置信度</param>
/// <param name="Features">人脸特征向量（w600k_r50 512-dim embedding）</param>
public sealed record FaceDetection(
    BoundingBox Bbox,
    float Confidence,
    byte[] Features
);
