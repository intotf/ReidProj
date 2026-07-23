namespace ReidProj.Models;

/// <summary>
/// 人物边界框
/// </summary>
public readonly record struct BoundingBox(
    int X,
    int Y,
    int Width,
    int Height
);
