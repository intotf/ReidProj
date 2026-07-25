using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;

namespace ReidFeature.Handlers;

/// <summary>
/// HTTP API 处理器 — 人物检测端点
/// </summary>
public static class DetectHandler
{
    /// <summary>
    /// 处理检测请求：通过原始二进制上传图片
    /// </summary>
    public static async Task<IResult> HandleImageAsync(
        HttpRequest request,
        YoloDetector yolo,
        ReIdExtractor reid,
        FaceDetector faceDetector,
        DetectionFlags? flags,
        ILogger<Program> logger)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            return Results.BadRequest(new ErrorResponse("请求体不能为空，请上传图片"));
        }

        Log.ImageRequestReceived(logger, request.ContentLength.Value);
        return await HandleStreamAsync(request.Body, yolo, reid, faceDetector, flags, logger);
    }

    /// <summary>
    /// 处理检测请求：通过图片 URL 下载后检测
    /// </summary>
    public static async Task<IResult> HandleUrlAsync(
        UrlDetectRequest request,
        YoloDetector yolo,
        ReIdExtractor reid,
        FaceDetector faceDetector,
        DetectionFlags? flags,
        ILogger<Program> logger,
        HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            return Results.BadRequest(new ErrorResponse("url 不能为空"));
        }

        try
        {
            var response = await httpClient.GetAsync(request.ImageUrl);
            using var imageStream = await response.EnsureSuccessStatusCode().Content.ReadAsStreamAsync();

            Log.ImageRequestReceived(logger, response.Content.Headers.ContentLength ?? 0);
            return await HandleStreamAsync(imageStream, yolo, reid, faceDetector, flags, logger);
        }
        catch (Exception ex)
        {
            Log.ImageDecodeFailed(logger, ex);
            return Results.BadRequest(new ErrorResponse("无法从 URL 下载图片"));
        }
    }

    private static async Task<IResult> HandleStreamAsync(
        Stream imageStream,
        YoloDetector yolo,
        ReIdExtractor reid,
        FaceDetector faceDetector,
        DetectionFlags? flags,
        ILogger<Program> logger)
    {
        var sw = Stopwatch.StartNew();
        Image<Rgb24> image;
        try
        {
            image = await Image.LoadAsync<Rgb24>(imageStream);
        }
        catch (Exception ex)
        {
            Log.ImageDecodeFailed(logger, ex);
            return Results.BadRequest(new ErrorResponse("不支持的图片格式"));
        }

        using (image)
        {
            var detections = yolo.DetectPersons(image);
            if (detections.Count == 0)
                return Results.Ok(new DetectResponse([]));

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
                    face = faceDetector.DetectBestFace(cropped, box.X, box.Y);

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
