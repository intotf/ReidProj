using SixLabors.ImageSharp;

namespace FaceFeature.Payloads;

/// <summary>
/// 人脸边界框
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
)
{
    /// <summary>
    /// 从 ImageSharp.Rectangle 隐式转换（内部检测矩形 → API 边界框）
    /// </summary>
    public static implicit operator BoundingBox(Rectangle rect)
        => new(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>
    /// 从 API 边界框隐式转换为 ImageSharp.Rectangle（内部处理统一使用 Rectangle）
    /// </summary>
    public static implicit operator Rectangle(BoundingBox box)
        => new(box.X, box.Y, box.Width, box.Height);
}
