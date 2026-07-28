using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ReidFeature.Helpers;

/// <summary>
/// ONNX 模型推理共用的图像预处理工具
/// </summary>
static class ImageProcessor
{
    /// <summary>
    /// Letterbox resize — 保持宽高比缩放到 targetSize，多余部分用灰色(114)填充
    /// </summary>
    public static Image<Rgb24> LetterboxResize(Image<Rgb24> src, int targetSize)
    {
        float scale = Math.Min((float)targetSize / src.Width, (float)targetSize / src.Height);
        int newW = (int)(src.Width * scale);
        int newH = (int)(src.Height * scale);

        using var resized = src.Clone(ctx => ctx.Resize(newW, newH, KnownResamplers.Bicubic));
        var canvas = new Image<Rgb24>(targetSize, targetSize, new Rgb24(114, 114, 114));
        int offsetX = (targetSize - newW) / 2;
        int offsetY = (targetSize - newH) / 2;

        canvas.Mutate(ctx => ctx.DrawImage(resized, new Point(offsetX, offsetY), 1f));
        return canvas;
    }
}
