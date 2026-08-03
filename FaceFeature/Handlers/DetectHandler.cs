using FaceFeature.Helpers;
using FaceFeature.Payloads;
using FaceFeature.Services;

namespace FaceFeature.Handlers;

/// <summary>
/// HTTP API 处理器 — 人脸检测端点
/// </summary>
public static class DetectHandler
{
    /// <summary>
    /// 处理检测请求：上传 H264 裸流帧，多帧融合后返回单个检测结果
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）；≤0 时解码输入流的所有帧</param>
    /// <param name="fusionFrames">融合帧数上限（&gt;0）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<FaceDetection?> HandleH264StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        double frameIntervalSeconds = 0.5,
        int fusionFrames = 30,
        CancellationToken cancellationToken = default)
    {
        if (context.Request.ContentLength == null || context.Request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            return null;
        }

        var frames = detectService.DetectFramesAsync(context.Request.Body, VideoCodec.H264, frameIntervalSeconds, cancellationToken);
        var fused = await FaceVideoFusion.FuseAsync(frames, fusionFrames > 0 ? fusionFrames : int.MaxValue, cancellationToken);
        if (fused is null)
        {
            return null;
        }

        return new FaceDetection(fused.Bbox, fused.Confidence, fused.Features, fused.Sharpness);
    }

    /// <summary>
    /// 处理检测请求：上传 H265 裸流帧，多帧融合后返回单个检测结果
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）；≤0 时解码输入流的所有帧</param>
    /// <param name="fusionFrames">融合帧数上限（&gt;0）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<FaceDetection?> HandleH265StreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        double frameIntervalSeconds = 0.5,
        int fusionFrames = 30,
        CancellationToken cancellationToken = default)
    {
        if (context.Request.ContentLength == null || context.Request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            return null;
        }

        var frames = detectService.DetectFramesAsync(context.Request.Body, VideoCodec.H265, frameIntervalSeconds, cancellationToken);
        var fused = await FaceVideoFusion.FuseAsync(frames, fusionFrames > 0 ? fusionFrames : int.MaxValue, cancellationToken);
        if (fused is null)
        {
            return null;
        }

        return new FaceDetection(fused.Bbox, fused.Confidence, fused.Features, fused.Sharpness);
    }

}
