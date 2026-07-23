using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ReidProj.Services;

public sealed class ImageUtils
{

    public const int YoloInputSize = 640;
    public const int ReIdHeight = 256;
    public const int ReIdWidth = 128;


    /// <summary>
    /// 从字节流解码为 RGB 图像
    /// </summary>
    public Image<Rgb24> DecodeToRgb(Stream stream)
    {
        var image = Image.Load<Rgb24>(stream);
        return image;
    }

    /// <summary>
    /// Letterbox resize 到 640×640，保持宽高比填充灰边
    /// </summary>
    public Image<Rgb24> LetterboxResize(Image<Rgb24> src, int targetSize)
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
    /// 提取 CHW tensor 并做 /255 归一化（YOLOv11 输入用）
    /// </summary>
    public float[] NormalizeToTensor(Image<Rgb24> image)
    {
        int h = image.Height, w = image.Width;
        var result = new float[3 * h * w];

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    int idx = y * w + x;
                    result[idx] = p.R / 255f;
                    result[h * w + idx] = p.G / 255f;
                    result[2 * h * w + idx] = p.B / 255f;
                }
            }
        });

        return result;
    }

    /// <summary>
    /// 提取原始 RGB 像素值为 [0,1] float CHW tensor（ReID 用，mean/std 已内嵌在 ONNX 图中）
    /// </summary>
    public float[] ExtractRawTensor(Image<Rgb24> image)
    {
        int h = image.Height, w = image.Width;
        var result = new float[3 * h * w];

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    int idx = y * w + x;
                    result[idx] = p.R / 255f;
                    result[h * w + idx] = p.G / 255f;
                    result[2 * h * w + idx] = p.B / 255f;
                }
            }
        });

        return result;
    }

    /// <summary>
    /// 裁剪人物区域
    /// </summary>
    public Image<Rgb24> CropRegion(Image<Rgb24> src, Rectangle rect)
    {
        int x = Math.Clamp(rect.X, 0, src.Width - 1);
        int y = Math.Clamp(rect.Y, 0, src.Height - 1);
        int w = Math.Max(1, Math.Min(rect.Width, src.Width - x));
        int h = Math.Max(1, Math.Min(rect.Height, src.Height - y));
        return src.Clone(ctx => ctx.Crop(new Rectangle(x, y, w, h)));
    }
}

public readonly record struct RectRecord(int X, int Y, int Width, int Height);
