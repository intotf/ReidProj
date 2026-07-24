using Microsoft.IO;
using ReidFeature.Helpers;
using ReidFeature.Models;
using ReidFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ReidFeature.Handlers;

public static class DetectHandler
{
    public static async Task<IResult> HandleAsync(
        HttpRequest request,
        RecyclableMemoryStreamManager streamManager,
        YoloDetector yolo,
        ReIdExtractor reid,
        FaceDetector faceDetector,
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
            var detections = yolo.Detect(image);
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

                // 人脸检测（坐标自动映射回原图）
                var face = faceDetector.DetectBestFace(cropped, box.X, box.Y);

                persons[i] = new PersonDetection(
                    Bbox: new BoundingBox(box.X, box.Y, box.Width, box.Height),
                    Confidence: conf,
                    Features: features,
                    Face: face
                );
            }

            Log.DetectionCompleted(logger, persons.Length);
            return Results.Ok(new DetectResponse(persons));
        }
    }
}
