using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ReidFeature.Helpers;

/// <summary>
/// ONNX 模型推理共用的图像预处理工具
/// </summary>
internal static class ImageProcessingHelper
{
    // ImageNet 标准化参数（YOLO 系列通用）
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

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

    /// <summary>
    /// 将 RGB 图像填充到 CHW float 数组，应用 ImageNet 标准化
    /// </summary>
    /// <param name="image">输入图像</param>
    /// <param name="destination">目标数组，长度必须 ≥ 3 × h × w</param>
    public static void NormalizeToTensor(Image<Rgb24> image, float[] destination)
    {
        int h = image.Height, w = image.Width;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    int idx = y * w + x;
                    destination[idx] = (p.R / 255f - Mean[0]) / Std[0];
                    destination[h * w + idx] = (p.G / 255f - Mean[1]) / Std[1];
                    destination[2 * h * w + idx] = (p.B / 255f - Mean[2]) / Std[2];
                }
            }
        });
    }
}
