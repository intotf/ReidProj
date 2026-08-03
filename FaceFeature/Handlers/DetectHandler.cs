using FaceFeature.Helpers;
using FaceFeature.Payloads;
using FaceFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.CompilerServices;

namespace FaceFeature.Handlers;

/// <summary>
/// HTTP API 处理器 — 人脸检测端点
/// </summary>
public static class DetectHandler
{
    /// <summary>
    /// 处理检测请求：通过原始二进制上传图片，检测面积最大的最佳人脸
    /// </summary>
    /// <param name="request">HTTP 请求体，包含原始图片二进制数据</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<FaceDetection?> HandleImageAsync(
        HttpRequest request,
        DetectService detectService,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            return null;
        }

        Image<Rgb24> image;
        try
        {
            image = await Image.LoadAsync<Rgb24>(request.Body, cancellationToken);
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.ImageDecodeFailed(logger, ex);
            return null;
        }

        using (image)
        {
            return detectService.DetectBestFace(image);
        }
    }

    /// <summary>
    /// 处理检测请求：通过图片 URL 下载后检测，检测面积最大的最佳人脸
    /// </summary>
    /// <param name="request">URL 检测请求，包含 ImageUrl 属性</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="httpClient">用于下载图片的 HTTP 客户端</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<FaceDetection?> HandleImageUrlAsync(
        UrlDetectRequest request,
        DetectService detectService,
        ILogger<Program> logger,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            return null;
        }

        Image<Rgb24> image;
        try
        {
            using var response = await httpClient.GetAsync(request.ImageUrl, cancellationToken);
            await using var imageStream = await response.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(cancellationToken);
            image = await Image.LoadAsync<Rgb24>(imageStream, cancellationToken);
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.ImageDecodeFailed(logger, ex);
            return null;
        }

        using (image)
        {
            return detectService.DetectBestFace(image);
        }
    }

    /// <summary>
    /// 处理检测请求：上传 H264 裸流帧，边解码边检测
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）；≤0 时解码输入流的所有帧</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static IAsyncEnumerable<FaceDetection> HandleH264StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        double frameIntervalSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        return HandleVideoAsync(context.Request, detectService, VideoCodec.H264, frameIntervalSeconds, logger, cancellationToken);
    }

    /// <summary>
    /// 处理检测请求：上传 H265 裸流帧，边解码边检测
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）；≤0 时解码输入流的所有帧</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static IAsyncEnumerable<FaceDetection> HandleH265StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        double frameIntervalSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        return HandleVideoAsync(context.Request, detectService, VideoCodec.H265, frameIntervalSeconds, logger, cancellationToken);
    }

    /// <summary>
    /// 处理检测请求：上传 H264/H265 裸流帧，解码后检测
    /// </summary>
    private static async IAsyncEnumerable<FaceDetection> HandleVideoAsync(
        HttpRequest request,
        DetectService detectService,
        VideoCodec codec,
        double frameIntervalSeconds,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            yield break;
        }

        await foreach (var image in VideoDecoder.DecodeFramesAsync(request.Body, codec, logger, frameIntervalSeconds, cancellationToken))
        {
            using (image)
            {
                var item = detectService.DetectBestFace(image, skipBlurry: true);
                if (item is not null)
                {
                    yield return item;
                }
            }
        }
    }
}
