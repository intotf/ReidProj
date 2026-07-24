using Microsoft.IO;
using ReidFeature.Models;
using ReidFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ReidFeature.Handlers;

public static class DetectHandler
{
    public static async Task<IResult> HandleAsync(
        HttpRequest request,
        RecyclableMemoryStreamManager streamManager,
        YoloDetector yolo,
        ReIdExtractor reid,
        ImageUtils imageUtils,
        ILogger<Program> logger)
    {
        using var ms = streamManager.GetStream("detect");
        await request.Body.CopyToAsync(ms);

        if (ms.Length == 0)
        {
            logger.LogWarning("请求体为空");
            return Results.BadRequest(new { error = "请求体不能为空，请上传图片" });
        }

        logger.LogInformation("收到图片请求: {Len} bytes", ms.Length);

        Image<Rgb24> image;
        try
        {
            ms.Position = 0;
            image = imageUtils.DecodeToRgb(ms);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "图片解码失败");
            return Results.BadRequest(new { error = "不支持的图片格式" });
        }

        using (image)
        {
            List<(Rectangle Box, float Confidence)> detections;
            try
            {
                detections = yolo.Detect(image);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "YOLO 检测失败");
                return Results.Problem("模型推理失败", statusCode: 500);
            }

            if (detections.Count == 0)
            {
                return Results.Ok(new DetectResponse([]));
            }

            var persons = new PersonDetection[detections.Count];
            for (int i = 0; i < detections.Count; i++)
            {
                var (box, conf) = detections[i];
                using var cropped = imageUtils.CropRegion(image, box);
                byte[] features = reid.ExtractFeatures(cropped);
                persons[i] = new PersonDetection(
                    Bbox: new BoundingBox(box.X, box.Y, box.Width, box.Height),
                    Confidence: conf,
                    Features: features
                );
            }

            logger.LogInformation("检测完成: {Cnt} 个人物", persons.Length);
            return Results.Ok(new DetectResponse(persons));
        }
    }
}
