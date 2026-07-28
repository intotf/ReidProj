using ReidFeature.Helpers;
using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ReidFeature.Services;

/// <summary>
/// 检测编排服务：YOLO 人物检测 → ReID 特征提取 → 人脸检测（可选）
/// </summary>
public sealed class DetectService
{
    private readonly YoloDetector _yolo;
    private readonly ReIdExtractor _reid;
    private readonly FaceDetector _faceDetector;
    private readonly ILogger<DetectService> _logger;

    /// <summary>
    /// 检测编排服务
    /// </summary>
    /// <param name="yolo"></param>
    /// <param name="reid"></param>
    /// <param name="faceDetector"></param>
    /// <param name="logger"></param>
    public DetectService(YoloDetector yolo, ReIdExtractor reid, FaceDetector faceDetector, ILogger<DetectService> logger)
    {
        _yolo = yolo;
        _reid = reid;
        _faceDetector = faceDetector;
        _logger = logger;
    }

    /// <summary>
    /// 对输入图像执行完整检测管线，并将检测结果可视化保存到 out/ 目录
    /// </summary>
    /// <param name="image">输入 RGB 图像</param>
    /// <param name="flags">检测功能标志位</param>
    /// <param name="frameIndex">帧索引（视频场景下传入当前帧序号；非视频场景默认为 0）</param>
    /// <returns>检测到的人物列表（可能为空）</returns>
    public IEnumerable<PersonDetection> DetectPersons(Image<Rgb24> image, DetectionFlags flags, int frameIndex = 0)
    {
#if DEBUG
        // 先收集所有检测结果
        var results = new List<PersonDetection>();
#endif

        using var enumerator = RunPipeline(image, flags, frameIndex).GetEnumerator();
        while (true)
        {
            var item = default(PersonDetection);
            try
            {
                if (!enumerator.MoveNext())
                {
                    break;
                }

                item = enumerator.Current;
            }
            catch (Exception ex)
            {
                Log.DetectPipelineFailed(_logger, ex);
                break;
            }

            if (item is not null)
            {
#if DEBUG
                results.Add(item);
#endif
                yield return item;
            }
        }

        // 可视化：绘制人物框（绿色）和人脸框（红色）
#if DEBUG
        if (results.Count > 0)
        {
            using var annotated = image.Clone();
            DrawDetectionBoxes(annotated, results);
            var outDir = Path.Combine(AppContext.BaseDirectory, "out");
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            annotated.SaveAsPng(path);
        }
#endif

    }

    /// <summary>
    /// 对输入图像执行完整检测管线
    /// </summary>
    /// <param name="image">输入 RGB 图像</param>
    /// <param name="flags">检测功能标志位</param>
    /// <param name="frameIndex">帧索引</param>
    /// <returns>检测到的人物列表（可能为空）</returns>
    private IEnumerable<PersonDetection> RunPipeline(Image<Rgb24> image, DetectionFlags flags, int frameIndex)
    {
        var detections = _yolo.DetectPersons(image);
        if (detections.Count == 0)
        {
            yield break;
        }

        for (int i = 0; i < detections.Count; i++)
        {
            var (box, conf) = detections[i];
            int x = Math.Clamp(box.X, 0, image.Width - 1);
            int y = Math.Clamp(box.Y, 0, image.Height - 1);
            int w = Math.Max(1, Math.Min(box.Width, image.Width - x));
            int h = Math.Max(1, Math.Min(box.Height, image.Height - y));

            using var cropped = image.Clone(ctx => ctx.Crop(new Rectangle(x, y, w, h)));

            FaceDetection? face = null;
            if (!flags.HasFlag(DetectionFlags.SkipFaceDetection))
            {
                face = _faceDetector.DetectBestFace(cropped, box.X, box.Y);
            }

            yield return new PersonDetection(
                FrameIndex: frameIndex,
                Bbox: new BoundingBox(box.X, box.Y, box.Width, box.Height),
                Confidence: conf,
                Features: _reid.ExtractFeatures(cropped),
                Face: face);
        }
    }

    /// <summary>
    /// 在图像上绘制人物边界框（绿色）和人脸边界框（红色）
    /// </summary>
    private static void DrawDetectionBoxes(Image<Rgb24> image, List<PersonDetection> detections)
    {
        var personColor = new Rgb24(0, 255, 0);
        var faceColor = new Rgb24(255, 0, 0);
        const int thickness = 2;

        foreach (var det in detections)
        {
            DrawRectangle(image, det.Bbox, personColor, thickness);

            if (det.Face is { } face)
            {
                DrawRectangle(image, face.Bbox, faceColor, thickness);
            }
        }
    }

    /// <summary>
    /// 在图像上绘制一个矩形边框
    /// </summary>
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
            // 上边
            int topY = y1 + t;
            if (topY < imgH)
                for (int x = x1 + t; x <= x2 - t; x++)
                    image[x, topY] = color;

            // 下边
            int bottomY = y2 - t;
            if (bottomY >= 0)
                for (int x = x1 + t; x <= x2 - t; x++)
                    image[x, bottomY] = color;

            // 左边
            int leftX = x1 + t;
            if (leftX < imgW)
                for (int y = y1 + t + 1; y <= y2 - t - 1; y++)
                    image[leftX, y] = color;

            // 右边
            int rightX = x2 - t;
            if (rightX >= 0)
                for (int y = y1 + t + 1; y <= y2 - t - 1; y++)
                    image[rightX, y] = color;
        }
    }
}
