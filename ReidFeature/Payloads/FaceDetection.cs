namespace ReidFeature.Payloads;

/// <summary>
/// 人脸检测结果 — 坐标相对于原始输入图像
/// </summary>
/// <param name="Bbox">人脸边界框</param>
/// <param name="Confidence">人脸置信度</param>
/// <param name="Features">可选的人脸特征向量（w600k_r50 512-dim embedding），未提取时为 null</param>
public readonly record struct FaceDetection(
    BoundingBox Bbox,
    float Confidence,
    byte[]? Features = null
);
