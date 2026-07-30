using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.CompilerServices;

namespace ReidFeature.Handlers;

/// <summary>
/// HTTP API 处理器 — 人物检测端点（仅视频流）
/// </summary>
public static class DetectHandler
{
    /// <summary>
    /// 处理 H264 视频流检测请求
    /// </summary>
    public static IAsyncEnumerable<PersonDetection> HandleH264StreamAsync(
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
    /// 处理 H265 视频流检测请求
    /// </summary>
    public static IAsyncEnumerable<PersonDetection> HandleH265StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        int frameIntervalSeconds = 5,
        DetectionFlags flags = DetectionFlags.All,
        CancellationToken cancellationToken = default)
    {
        return HandleVideoAsync(context.Request, detectService, flags, VideoCodec.H265, frameIntervalSeconds, logger, cancellationToken);
    }

    private static async IAsyncEnumerable<PersonDetection> HandleVideoAsync(
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

        // 重置跟踪状态以处理新视频流
        detectService.Reset();

        var frameIdx = 0;
        var enumerable = VideoDecoder.DecodeFramesAsync(request.Body, codec, logger, frameIntervalSeconds, cancellationToken);
        await using var enumerator = enumerable.GetAsyncEnumerator(cancellationToken);

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
                // YOLO → ByteTrack → 缓存帧
                detectService.ProcessVideoFrame(image, frameIdx);
            }

            // StopOnFirstFrameHit：检测到目标就提前结束
            if (flags.HasFlag(DetectionFlags.StopOnFirstFrameHit))
            {
                var allTracks = detectService.FlushCompletedTracks();
                if (allTracks.Count > 0)
                {
                    foreach (var item in allTracks)
                        yield return item;
                    yield break;
                }
            }

            frameIdx++;
        }

        // 视频流结束后，融合所有已完成 Track 返回
        var results = detectService.FlushCompletedTracks();
        foreach (var item in results)
            yield return item;
    }
}
