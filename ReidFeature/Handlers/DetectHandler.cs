using Microsoft.IO;
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
            logger.LogWarning("请求体为空");
            return Results.BadRequest(new { error = "请求体不能为空，请上传图片" });
        }

        logger.LogInformation("收到图片请求: {Len} bytes", ms.Length);

        Image<Rgb24> image;
        try
        {
            ms.Position = 0;
            image = Image.Load<Rgb24>(ms);
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
                int x = Math.Clamp(box.X, 0, image.Width - 1);
                int y = Math.Clamp(box.Y, 0, image.Height - 1);
                int w = Math.Max(1, Math.Min(box.Width, image.Width - x));
                int h = Math.Max(1, Math.Min(box.Height, image.Height - y));

                using var cropped = image.Clone(ctx => ctx.Crop(new Rectangle(x, y, w, h)));

                // ReID 特征提取
                byte[] features = reid.ExtractFeatures(cropped);

                // 人脸检测（在裁剪图上，坐标映射回原图）
                FaceDetection? face = null;
                try
                {
                    var faces = faceDetector.Detect(cropped);
                    if (faces.Count > 0)
                    {
                        var best = faces[0]; // 取置信度最高的人脸
                        face = new FaceDetection(
                            new BoundingBox(
                                box.X + best.Bbox.X,
                                box.Y + best.Bbox.Y,
                                best.Bbox.Width,
                                best.Bbox.Height
                            ),
                            best.Confidence
                        );
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "人脸检测失败, 人物 #{Idx}", i);
                }

                persons[i] = new PersonDetection(
                    Bbox: new BoundingBox(box.X, box.Y, box.Width, box.Height),
                    Confidence: conf,
                    Features: features,
                    Face: face
                );
            }

            logger.LogInformation("检测完成: {Cnt} 个人物", persons.Length);
            return Results.Ok(new DetectResponse(persons));
        }
    }
}
