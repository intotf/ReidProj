namespace ReidFeature.Models;

/// <summary>
/// 单个人物的检测+特征结果
/// </summary>
public sealed record PersonDetection(
    BoundingBox Bbox,
    float Confidence,
    byte[] Features
);
