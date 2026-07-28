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
    /// 处理检测请求：通过原始二进制上传图片
    /// </summary>
    /// <param name="request">HTTP 请求体，包含原始图片二进制数据</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async IAsyncEnumerable<FaceDetection> HandleImageAsync(
        HttpRequest request,
        DetectService detectService,
        ILogger<Program> logger,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            yield break;
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
            yield break;
        }

        using (image)
        {
            foreach (var item in detectService.DetectFaces(image, DetectionFlags.All))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
    }

    /// <summary>
    /// 处理检测请求：通过图片 URL 下载后检测
    /// </summary>
    /// <param name="request">URL 检测请求，包含 ImageUrl 属性</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="httpClient">用于下载图片的 HTTP 客户端</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async IAsyncEnumerable<FaceDetection> HandleImageUrlAsync(
        UrlDetectRequest request,
        DetectService detectService,
        ILogger<Program> logger,
        HttpClient httpClient,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            yield break;
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
            yield break;
        }

        using (image)
        {
            foreach (var item in detectService.DetectFaces(image, DetectionFlags.All))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
    }

    /// <summary>
    /// 处理检测请求：上传 H264 裸流帧，边解码边检测
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）；≤0 时解码输入流的所有帧</param>
    /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 2=StopOnFirstFrameHit(首帧命中即停)</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static IAsyncEnumerable<FaceDetection> HandleH264StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        int frameIntervalSeconds = 5,
        DetectionFlags flags = DetectionFlags.All,
        CancellationToken cancellationToken = default)
    {
        return HandleVideoAsync(context.Request, detectService, flags, VideoCodec.H264, frameIntervalSeconds, logger, cancellationToken);
    }

    /// <summary>
    /// 处理检测请求：上传 H265 裸流帧，边解码边检测
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）；≤0 时解码输入流的所有帧</param>
    /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 2=StopOnFirstFrameHit(首帧命中即停)</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static IAsyncEnumerable<FaceDetection> HandleH265StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        int frameIntervalSeconds = 5,
        DetectionFlags flags = DetectionFlags.All,
        CancellationToken cancellationToken = default)
    {
        return HandleVideoAsync(context.Request, detectService, flags, VideoCodec.H265, frameIntervalSeconds, logger, cancellationToken);
    }

    /// <summary>
    /// 处理检测请求：上传 H264/H265 裸流帧，解码后检测
    /// </summary>
    private static async IAsyncEnumerable<FaceDetection> HandleVideoAsync(
        HttpRequest request,
        DetectService detectService,
        DetectionFlags flags,
        VideoCodec codec,
        int frameIntervalSeconds,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            yield break;
        }

        var frameIdx = 0;
        var enumerable = VideoDecoder.DecodeFramesAsync(request.Body, codec, logger, frameIntervalSeconds, cancellationToken);
        await using var enumerator = enumerable.GetAsyncEnumerator(cancellationToken);

        var hasHit = false;
        while (true)
        {
            Image<Rgb24> image;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;
                image = enumerator.Current;
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Log.VideoDecodeFailed(logger, ex);
                yield break;
            }

            using (image)
            {
                foreach (var item in detectService.DetectFaces(image, flags, frameIdx))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hasHit = true;
                    yield return item;
                }
            }

            // StopOnFirstFrameHit：一帧检测到目标就提前结束视频解码
            if (hasHit && flags.HasFlag(DetectionFlags.StopOnFirstFrameHit))
                yield break;

            frameIdx++;
        }
    }
}
