namespace ReidFeature.Payloads;

/// <summary>
/// 人脸检测结果 — 坐标相对于原始输入图像
/// </summary>
/// <param name="Bbox">人脸边界框</param>
/// <param name="Confidence">人脸置信度</param>
public readonly record struct FaceDetection(
    BoundingBox Bbox,
    float Confidence
);
