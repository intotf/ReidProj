using FaceFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceFeature.Services;

/// <summary>
/// 人脸检测编排服务：SCRFD 全图人脸检测 → ArcFace 逐人脸特征提取
/// 不依赖 YOLO 人物检测，直接在全图上检测人脸。
/// </summary>
public sealed class DetectService
{
    private readonly FaceDetector _faceDetector;
    private readonly FaceExtractor _faceExtractor; 

    /// <summary>
    /// 人脸检测编排服务
    /// </summary>
    /// <param name="faceDetector"></param>
    /// <param name="faceExtractor"></param> 
    public DetectService(FaceDetector faceDetector, FaceExtractor faceExtractor )
    {
        _faceDetector = faceDetector;
        _faceExtractor = faceExtractor; 
    }

    /// <summary>
    /// 对输入图像检测面积最大的最佳人脸（性能优先——避免全量特征提取）
    /// </summary>
    /// <param name="image">输入 RGB 图像</param> 
    /// <returns>最佳人脸检测结果，无人脸时返回 null</returns>
    public FaceDetection? DetectBestFace(Image<Rgb24> image)
    {
        var best = _faceDetector.DetectBest(image);
        if (best is null)
            return null;

        var (box, conf) = best.Value;
        var faceRect = new Rectangle(
            Math.Clamp(box.X, 0, image.Width - 1),
            Math.Clamp(box.Y, 0, image.Height - 1),
            Math.Max(1, Math.Min(box.Width, image.Width - box.X)),
            Math.Max(1, Math.Min(box.Height, image.Height - box.Y)));

        var result = new FaceDetection(
            Bbox: new BoundingBox(faceRect.X, faceRect.Y, faceRect.Width, faceRect.Height),
            Confidence: conf,
            Features: _faceExtractor.ExtractFeatures(image, faceRect));

#if DEBUG
        using var annotated = image.Clone();
        DrawDetectionBoxes(annotated, [result]);
        var outDir = Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        annotated.SaveAsPng(path);
#endif

        return result;
    }
     
    /// <summary>
    /// 在图像上绘制人脸边界框（红色）
    /// </summary>
    private static void DrawDetectionBoxes(Image<Rgb24> image, List<FaceDetection> detections)
    {
        var faceColor = new Rgb24(255, 0, 0);
        const int thickness = 2;
        foreach (var det in detections)
        {
            DrawRectangle(image, det.Bbox, faceColor, thickness);
        }
    }

    private static void DrawRectangle(Image<Rgb24> image, BoundingBox bbox, Rgb24 color, int thickness)
    {
        int imgW = image.Width;
        int imgH = image.Height;
        int x1 = Math.Clamp(bbox.X, 0, imgW - 1);
        int y1 = Math.Clamp(bbox.Y, 0, imgH - 1);
        int x2 = Math.Clamp(bbox.X + bbox.Width - 1, 0, imgW - 1);
        int y2 = Math.Clamp(bbox.Y + bbox.Height - 1, 0, imgH - 1);

        for (int t = 0; t < thickness; t++)
        {
            int topY = y1 + t;
            if (topY < imgH)
                for (int x = x1 + t; x <= x2 - t; x++)
                    image[x, topY] = color;

            int bottomY = y2 - t;
            if (bottomY >= 0)
                for (int x = x1 + t; x <= x2 - t; x++)
                    image[x, bottomY] = color;

            int leftX = x1 + t;
            if (leftX < imgW)
                for (int y = y1 + t + 1; y <= y2 - t - 1; y++)
                    image[leftX, y] = color;

            int rightX = x2 - t;
            if (rightX >= 0)
                for (int y = y1 + t + 1; y <= y2 - t - 1; y++)
                    image[rightX, y] = color;
        }
    }
}
