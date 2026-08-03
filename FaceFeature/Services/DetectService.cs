using FaceFeature.Helpers;
using FaceFeature.Payloads;
using Microsoft.Extensions.Options;
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
    private readonly FaceQualityOptions _qualityOptions;
    private readonly ILogger<DetectService> _logger;

    /// <summary>
    /// 人脸检测编排服务
    /// </summary>
    /// <param name="faceDetector"></param>
    /// <param name="faceExtractor"></param> 
    /// <param name="qualityOptions">清晰度筛选配置</param>
    /// <param name="logger">日志记录器</param>
    public DetectService(
        FaceDetector faceDetector,
        FaceExtractor faceExtractor,
        IOptions<FaceQualityOptions> qualityOptions,
        ILogger<DetectService> logger)
    {
        _faceDetector = faceDetector;
        _faceExtractor = faceExtractor;
        _qualityOptions = qualityOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// 对输入图像检测面积最大的最佳人脸（性能优先——避免全量特征提取）
    /// </summary>
    /// <param name="image">输入 RGB 图像</param>
    /// <param name="skipBlurry">为 true 时，清晰度低于阈值的模糊人脸直接返回 null（视频流逐帧筛选）</param>
    /// <returns>最佳人脸检测结果（含清晰度分数），无人脸或模糊被跳过时返回 null</returns>
    public FaceDetection? DetectBestFace(Image<Rgb24> image, bool skipBlurry = false)
    {
        var best = _faceDetector.DetectBest(image);
        if (best is null)
            return null;

        using var extraction = _faceExtractor.AlignAndScore(image, best);

        if (skipBlurry && _qualityOptions.Enabled && extraction.Sharpness < _qualityOptions.SharpnessThreshold)
        {
            Log.FaceSkippedBlurry(_logger, extraction.Sharpness, _qualityOptions.SharpnessThreshold);
            return null;
        }

        var features = _faceExtractor.ExtractFeatures(extraction.Aligned);

        var box = best.Bbox;
        var faceRect = new Rectangle(
            Math.Clamp(box.X, 0, image.Width - 1),
            Math.Clamp(box.Y, 0, image.Height - 1),
            Math.Max(1, Math.Min(box.Width, image.Width - box.X)),
            Math.Max(1, Math.Min(box.Height, image.Height - box.Y)));

        var result = new FaceDetection(
            Bbox: new BoundingBox(faceRect.X, faceRect.Y, faceRect.Width, faceRect.Height),
            Confidence: best.Confidence,
            Features: features,
            Sharpness: extraction.Sharpness);

#if DEBUG
        using var annotated = image.Clone();
        DrawDetectionBoxes(annotated, [result]);
        if (best.Keypoints is { } keypoints)
        {
            DrawLandmarks(annotated, keypoints);
        }
        var outDir = Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        annotated.SaveAsPng(Path.Combine(outDir, $"{stamp}.png"));
        extraction.Aligned.SaveAsPng(Path.Combine(outDir, $"aligned_{stamp}.png"));
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

    /// <summary>
    /// 在图像上绘制 5 个关键点（绿色小方块），用于调试校验对齐质量
    /// </summary>
    private static void DrawLandmarks(Image<Rgb24> image, ReadOnlySpan<PointF> keypoints)
    {
        var color = new Rgb24(0, 255, 0);
        foreach (var p in keypoints)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = (int)p.X + dx;
                    int y = (int)p.Y + dy;
                    if (x >= 0 && y >= 0 && x < image.Width && y < image.Height)
                        image[x, y] = color;
                }
            }
        }
    }
}
