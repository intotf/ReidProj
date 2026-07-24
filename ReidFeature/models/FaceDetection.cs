namespace ReidFeature.Models;

/// <summary>
/// 人脸检测结果 — 坐标相对于原始输入图像
/// </summary>
public readonly record struct FaceDetection(
    BoundingBox Bbox,
    float Confidence
);
