using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
    /// <param name="request">HTTP 请求体，包含原始图片二进制数据</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测)</param>
    /// <param name="logger">日志记录器</param>
    public static async Task<IResult> HandleImageAsync(
        HttpRequest request,
        DetectService detectService,
        DetectionFlags? flags,
        ILogger<Program> logger)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            return Results.BadRequest(new ErrorResponse("请求体不能为空，请上传图片"));
        }

        Log.ImageRequestReceived(logger, request.ContentLength.Value);
        var sw = Stopwatch.StartNew();

        Image<Rgb24> image;
        try
        {
            image = await Image.LoadAsync<Rgb24>(request.Body);
        }
        catch (Exception ex)
        {
            Log.ImageDecodeFailed(logger, ex);
            return Results.BadRequest(new ErrorResponse("不支持的图片格式"));
        }

        using (image)
        {
            return Detect(image, detectService, flags, sw, logger);
        }
    }

    /// <summary>
    /// 处理检测请求：通过图片 URL 下载后检测
    /// </summary>
    /// <param name="request">URL 检测请求，包含 ImageUrl 属性</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测)</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="httpClient">用于下载图片的 HTTP 客户端</param>
    public static async Task<IResult> HandleUrlAsync(
        UrlDetectRequest request,
        DetectService detectService,
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
            var sw = Stopwatch.StartNew();
            var response = await httpClient.GetAsync(request.ImageUrl);
            using var imageStream = await response.EnsureSuccessStatusCode().Content.ReadAsStreamAsync();

            Log.ImageRequestReceived(logger, response.Content.Headers.ContentLength ?? 0);

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
                return Detect(image, detectService, flags, sw, logger);
            }
        }
        catch (Exception ex)
        {
            Log.ImageDecodeFailed(logger, ex);
            return Results.BadRequest(new ErrorResponse("无法从 URL 下载图片"));
        }
    }

    /// <summary>
    /// 处理检测请求：上传 H264/H265 裸流帧，解码后检测
    /// </summary>
    /// <param name="request">HTTP 请求体，包含 H264 或 H265 裸流数据</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测)</param>
    /// <param name="codec">视频编码格式。可取值: 0=H264(原始 H264 裸流 Annex B), 1=H265(原始 H265/HEVC 裸流)</param>
    /// <param name="logger">日志记录器</param>
    public static async Task<IResult> HandleVideoAsync(
        HttpRequest request,
        DetectService detectService,
        DetectionFlags? flags,
        VideoCodec codec,
        ILogger<Program> logger)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var image = await VideoDecoder.DecodeSingleFrameAsync(request.Body, codec, logger);
            return Detect(image, detectService, flags, sw, logger);
        }
        catch (InvalidDataException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    /// <summary>
    /// 执行检测并拼接响应
    /// </summary>
    private static IResult Detect(
        Image<Rgb24> image,
        DetectService detectService,
        DetectionFlags? flags,
        Stopwatch sw,
        ILogger<Program> logger)
    {
        var persons = detectService.Detect(image, flags);

        sw.Stop();
        Log.DetectionCompleted(logger, persons.Length, sw.Elapsed.TotalMilliseconds);

        return Results.Ok(new DetectResponse(persons));
    }
}
