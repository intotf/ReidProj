using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;

namespace ReidFeature.Handlers;

/// <summary>
/// HTTP API 处理器 —— 人物检测端点（仅视频流，编码由 VideoDecoder 自动嗅探）
/// </summary>
public static class DetectHandler
{
    /// <summary>
    /// 处理视频流检测请求（H264/H265 裸流均可，编码自动识别）
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧），如 0.5 表示每 0.5 秒一帧；≤0 时解码全部帧</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>检测到的人物列表（含四维特征包）；请求体为空或视频解码失败时返回空列表</returns>
    public static async Task<List<PersonDetection>> HandleStreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        double frameIntervalSeconds = 0.5,
        CancellationToken cancellationToken = default)
    {
        // 防御无效参数：NaN/±Infinity 统一按 0（解码全部帧）处理，并 clamp 到合理上限
        if (double.IsNaN(frameIntervalSeconds) || double.IsInfinity(frameIntervalSeconds))
        {
            frameIntervalSeconds = 0;
        }
        frameIntervalSeconds = Math.Clamp(frameIntervalSeconds, 0, 3600);

        // 解码 → 逐帧检测/跟踪/缓存（统一由 DetectService 处理）
        if (!await detectService.ProcessVideoStreamAsync(
            context.Request, logger, frameIntervalSeconds, cancellationToken))
        {
            return [];
        }

        // 视频流结束后，融合所有已完成 Track 返回
        return detectService.FlushCompletedTracks();
    }
}
