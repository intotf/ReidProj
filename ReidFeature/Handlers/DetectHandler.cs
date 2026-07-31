using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;

namespace ReidFeature.Handlers;

/// <summary>
/// HTTP API 处理器 — 人物检测端点（仅视频流）
/// </summary>
public static class DetectHandler
{
    /// <summary>
    /// 处理 H264 视频流检测请求
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧），如 0.5 表示每 0.5 秒一帧；≤0 时解码全部帧</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>检测到的人物列表（含四维特征包）；请求体为空或视频解码失败时返回空列表</returns>
    public static async Task<List<PersonDetection>> HandleH264StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        double frameIntervalSeconds = 0.5,
        CancellationToken cancellationToken = default)
    {
        return await HandleVideoAsync(context.Request, detectService, VideoCodec.H264, frameIntervalSeconds, logger, cancellationToken);
    }

    /// <summary>
    /// 处理 H265 视频流检测请求
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧），如 0.5 表示每 0.5 秒一帧；≤0 时解码全部帧</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>检测到的人物列表（含四维特征包）；请求体为空或视频解码失败时返回空列表</returns>
    public static async Task<List<PersonDetection>> HandleH265StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        double frameIntervalSeconds = 0.5,
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
        // 解码 → 逐帧检测/跟踪/缓存（统一由 DetectService 处理）
        if (!await detectService.ProcessVideoStreamAsync(request, codec, logger, frameIntervalSeconds, cancellationToken))
        {
            return [];
        }

        // 视频流结束后，融合所有已完成 Track 返回
        return detectService.FlushCompletedTracks();
    }
}
