using SixLabors.ImageSharp;

namespace FaceFeature.Payloads;

/// <summary>
/// 人脸检测结果 — 边界框、置信度与 5 关键点（原图坐标；无关键点模型时为 null）
/// </summary>
/// <param name="Bbox">人脸边界框</param>
/// <param name="Confidence">人脸置信度</param>
/// <param name="Keypoints">5 个关键点（左眼、右眼、鼻尖、左嘴角、右嘴角），无关键点输出时为 null</param>
public sealed record FaceBox(Rectangle Bbox, float Confidence, PointF[]? Keypoints);
