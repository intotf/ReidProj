using FaceFeature.Helpers;
using FaceFeature.Payloads;
using FaceFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceFeature.Handlers;

/// <summary>
/// HTTP API 处理器 — 人脸检测端点（视频编码由 VideoDecoder 自动嗅探，无需路由区分）
/// </summary>
public static class DetectHandler
{
    /// <summary>
    /// 处理检测请求：上传 H264/H265 裸流，多帧融合后返回单个检测结果
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）；≤0 时解码输入流的所有帧</param>
    /// <param name="fusionFrames">融合帧数上限（&gt;0）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<FaceDetection?> HandleStreamAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        double frameIntervalSeconds = 0.5,
        int fusionFrames = 30,
        CancellationToken cancellationToken = default)
    {
        var frames = detectService.DetectFramesAsync(context.Request.Body, frameIntervalSeconds, cancellationToken);
        var fused = await FaceVideoFusion.FuseAsync(frames, fusionFrames > 0 ? fusionFrames : int.MaxValue, cancellationToken);
        if (fused is null)
        {
            return null;
        }

        return new FaceDetection(fused.Bbox, fused.Confidence, fused.Features, fused.Sharpness);
    }

    /// <summary>
    /// 处理图像检测请求：上传 JPEG/PNG 等静态图片，解码后检测面积最大的最佳人脸
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<FaceDetection?> HandleImageAsync(
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        Image<Rgb24> image;
        try
        {
            image = await Image.LoadAsync<Rgb24>(context.Request.Body, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.ImageDecodeFailed(logger, ex);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        using (image)
        {
            return detectService.DetectBestFace(image);
        }
    }
}
