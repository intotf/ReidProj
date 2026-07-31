using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ReidFeature.Handlers;

/// <summary>
/// HTTP API 处理器 — 人物检测端点（仅视频流）
/// </summary>
public static class DetectHandler
{
    /// <summary>
    /// 处理 H264 视频流检测请求
    /// </summary>
    public static async Task<List<PersonDetection>> HandleH264StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        double frameIntervalSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        return await HandleVideoAsync(context.Request, detectService, VideoCodec.H264, frameIntervalSeconds, logger, cancellationToken);
    }

    /// <summary>
    /// 处理 H265 视频流检测请求
    /// </summary>
    public static async Task<List<PersonDetection>> HandleH265StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        double frameIntervalSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        return await HandleVideoAsync(context.Request, detectService, VideoCodec.H265, frameIntervalSeconds, logger, cancellationToken);
    }

    private static async Task<List<PersonDetection>> HandleVideoAsync(
        HttpRequest request,
        DetectService detectService,
        VideoCodec codec,
        double frameIntervalSeconds,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            return [];
        }

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
                return [];
            }

            using (image)
            {
                // YOLO → ByteTrack → 缓存帧
                detectService.ProcessVideoFrame(image, frameIdx);
            }

            frameIdx++;
        }

        // 视频流结束后，融合所有已完成 Track 返回
        return detectService.FlushCompletedTracks();
    }
}
