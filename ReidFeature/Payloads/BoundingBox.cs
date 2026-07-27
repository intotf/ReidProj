namespace ReidFeature.Payloads;

/// <summary>
/// 人物边界框
/// </summary>
/// <param name="X">左上角 X 坐标</param>
/// <param name="Y">左上角 Y 坐标</param>
/// <param name="Width">边界框宽度</param>
/// <param name="Height">边界框高度</param>
public readonly record struct BoundingBox(
    int X,
    int Y,
    int Width,
    int Height
);
