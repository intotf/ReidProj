using SixLabors.ImageSharp;

namespace ReidFeature.Helpers;

/// <summary>
/// 边界框工具 — 将检测框 Clamp 到图像边界内，避免越界导致图像处理抛异常
/// </summary>
public static class BoundingBoxHelper
{
    /// <summary>
    /// 将 bbox 裁剪到图像边界内
    /// </summary>
    /// <param name="rect">原始检测框</param>
    /// <param name="imageWidth">图像宽度</param>
    /// <param name="imageHeight">图像高度</param>
    /// <returns>Clamp 后的边界框（宽高恒 ≥ 1）；图像尺寸无效时返回空框</returns>
    public static Rectangle ClampToBounds(Rectangle rect, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return Rectangle.Empty;
        }

        int x = Math.Clamp(rect.X, 0, imageWidth - 1);
        int y = Math.Clamp(rect.Y, 0, imageHeight - 1);
        int right = Math.Clamp(rect.X + rect.Width, x + 1, imageWidth);
        int bottom = Math.Clamp(rect.Y + rect.Height, y + 1, imageHeight);

        return new Rectangle(x, y, right - x, bottom - y);
    }
}
