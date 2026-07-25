using Microsoft.IO;
using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;

namespace ReidFeature.Handlers;

/// <summary>
/// HTTP API 处理器 — 人物检测端点 /detect
/// </summary>
public static class DetectHandler
{
    /// <summary>
    /// 处理检测请求：YOLO 人物检测 → ReID 特征提取 → 人脸检测（可选）
    /// </summary>
    /// <param name="request">HTTP 请求，包含图片二进制数据</param>
    /// <param name="streamManager">可回收内存流管理器</param>
    /// <param name="yolo">YOLO 人物检测器</param>
    /// <param name="reid">ReID 特征提取器</param>
    /// <param name="faceDetector">人脸检测器</param>
    /// <param name="flags">功能开关标志（可选），例如 ?flags=SkipFaceDetection 跳过人脸检测</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>检测响应，包含人物框、特征向量和人脸信息（可选）</returns>
    public static async Task<IResult> HandleAsync(
        HttpRequest request,
        RecyclableMemoryStreamManager streamManager,
        YoloDetector yolo,
        ReIdExtractor reid,
        FaceDetector faceDetector,
        DetectionFlags? flags,
        ILogger<Program> logger)
    {
        using var ms = streamManager.GetStream("detect");
        await request.Body.CopyToAsync(ms);

        if (ms.Length == 0)
        {
            Log.RequestBodyEmpty(logger);
            return Results.BadRequest(new { error = "请求体不能为空，请上传图片" });
        }

        Log.ImageRequestReceived(logger, ms.Length);

        var sw = Stopwatch.StartNew();
        Image<Rgb24> image;
        try
        {
            ms.Position = 0;
            image = Image.Load<Rgb24>(ms);
        }
        catch (Exception ex)
        {
            Log.ImageDecodeFailed(logger, ex);
            return Results.BadRequest(new { error = "不支持的图片格式" });
        }

        using (image)
        {
            var detections = yolo.DetectPersons(image);
            if (detections.Count == 0)
            {
                return Results.Ok(new DetectResponse([]));
            }

            var persons = new PersonDetection[detections.Count];
            for (int i = 0; i < detections.Count; i++)
            {
                var (box, conf) = detections[i];
                int x = Math.Clamp(box.X, 0, image.Width - 1);
                int y = Math.Clamp(box.Y, 0, image.Height - 1);
                int w = Math.Max(1, Math.Min(box.Width, image.Width - x));
                int h = Math.Max(1, Math.Min(box.Height, image.Height - y));

                using var cropped = image.Clone(ctx => ctx.Crop(new Rectangle(x, y, w, h)));

                // ReID 特征提取
                var features = reid.ExtractFeatures(cropped);

                // 人脸检测（坐标自动映射回原图，可通过 ?flags=SkipFaceDetection 跳过）
                FaceDetection? face = null;
                if (flags?.HasFlag(DetectionFlags.SkipFaceDetection) != true)
                {
                    face = faceDetector.DetectBestFace(cropped, box.X, box.Y);
                }

                persons[i] = new PersonDetection(
                    Bbox: new BoundingBox(box.X, box.Y, box.Width, box.Height),
                    Confidence: conf,
                    Features: features,
                    Face: face
                );
            }

            Log.DetectionCompleted(logger, persons.Length, sw.Elapsed.TotalMilliseconds);
            return Results.Ok(new DetectResponse(persons));
        }
    }
}
