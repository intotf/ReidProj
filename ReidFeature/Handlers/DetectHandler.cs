using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.CompilerServices;

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
    /// <param name="cancellationToken">取消令牌</param>
    public static async IAsyncEnumerable<PersonDetection> HandleImageAsync(
        HttpRequest request,
        DetectService detectService,
        DetectionFlags? flags,
        ILogger<Program> logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
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
            foreach (var item in EnumerateDetections(image, detectService, flags))
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
    /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测)</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="httpClient">用于下载图片的 HTTP 客户端</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async IAsyncEnumerable<PersonDetection> HandleImageUrlAsync(
        UrlDetectRequest request,
        DetectService detectService,
        DetectionFlags? flags,
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
            foreach (var item in EnumerateDetections(image, detectService, flags))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
    }

    /// <summary>
    /// 处理检测请求：上传 H264 裸流帧，解码后检测
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）</param>
    /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测)</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static IAsyncEnumerable<PersonDetection> HandleH264StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        int frameIntervalSeconds = 5,
        DetectionFlags? flags = null,
        CancellationToken cancellationToken = default)
    {
        return HandleVideoAsync(context.Request, detectService, flags, VideoCodec.H264, frameIntervalSeconds, logger, cancellationToken);
    }

    /// <summary>
    /// 处理检测请求：上传 H265 裸流帧，解码后检测
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）</param>
    /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测)</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static IAsyncEnumerable<PersonDetection> HandleH265StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        int frameIntervalSeconds = 5,
        DetectionFlags? flags = null,
        CancellationToken cancellationToken = default)
    {
        return HandleVideoAsync(context.Request, detectService, flags, VideoCodec.H265, frameIntervalSeconds, logger, cancellationToken);
    }

    /// <summary>
    /// 处理检测请求：上传 H264/H265 裸流帧，解码后检测
    /// </summary>
    /// <param name="request">HTTP 请求体，包含 H264 或 H265 裸流数据</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测)</param>
    /// <param name="codec">视频编码格式。可取值: 0=H264(原始 H264 裸流 Annex B), 1=H265(原始 H265/HEVC 裸流)</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="cancellationToken">取消令牌</param>
    private static async IAsyncEnumerable<PersonDetection> HandleVideoAsync(
        HttpRequest request,
        DetectService detectService,
        DetectionFlags? flags,
        VideoCodec codec,
        int frameIntervalSeconds,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            yield break;
        }

        var enumerable = VideoDecoder.DecodeFramesAsync(request.Body, codec, logger, frameIntervalSeconds, cancellationToken);
        var enumerator = enumerable.GetAsyncEnumerator(cancellationToken);
        await using (enumerator)
        {
            while (true)
            {
                Image<Rgb24> image;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }
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
                    foreach (var item in EnumerateDetections(image, detectService, flags))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return item;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 逐项安全枚举检测结果，异常时静默终止
    /// </summary>
    private static IEnumerable<PersonDetection> EnumerateDetections(
        Image<Rgb24> image,
        DetectService detectService,
        DetectionFlags? flags)
    {
        using var enumerator = detectService.Detect(image, flags).GetEnumerator();
        while (true)
        {
            PersonDetection item;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                item = enumerator.Current;
            }
            catch (Exception)
            {
                yield break;
            }
            yield return item;
        }
    }
}
