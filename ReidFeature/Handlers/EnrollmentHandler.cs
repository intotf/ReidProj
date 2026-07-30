using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ReidFeature.Handlers;

/// <summary>
/// 家庭成员注册处理器
/// 上传 H264/H265 视频流 → 检测 → 跟踪 → 特征融合 → 存入 Gallery
/// </summary>
public static class EnrollmentHandler
{
    /// <summary>
    /// 处理注册请求
    /// </summary>
    /// <param name="familyProvider">家庭成员提供者</param>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="groupId">分组 ID</param>
    /// <param name="memberName">成员名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 响应（200 + memberId，或 400）</returns>
    public static async Task<IResult> HandleEnrollAsync(
        IFamilyMemberProvider familyProvider,
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        string groupId,
        string memberName,
        CancellationToken cancellationToken = default)
    {
        var request = context.Request;

        if (request.ContentLength == null || request.ContentLength == 0)
        {
            Log.RequestBodyEmpty(logger);
            return Results.BadRequest("请求体为空");
        }

        if (string.IsNullOrWhiteSpace(memberName))
        {
            return Results.BadRequest("memberName 不能为空");
        }

        // 从 Content-Type 推断编码，默认 H264
        var contentType = request.ContentType?.ToLowerInvariant() ?? "";
        var codec = contentType switch
        {
            string c when c.Contains("h265") || c.Contains("hevc") => VideoCodec.H265,
            _ => VideoCodec.H264,
        };

        // 处理视频流
        detectService.Reset();
        int frameIdx = 0;

        var enumerable = VideoDecoder.DecodeFramesAsync(
            request.Body, codec, logger, frameIntervalSeconds: 0, cancellationToken);
        await using var enumerator = enumerable.GetAsyncEnumerator(cancellationToken);

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
                return Results.BadRequest("视频解码失败");
            }

            using (image)
            {
                detectService.ProcessVideoFrame(image, frameIdx++);
            }
        }

        // 获取完成的 Track（取主导 Track）
        var tracks = detectService.FlushCompletedTracks(minFrames: 5);
        if (tracks.Count == 0)
        {
            return Results.BadRequest("未检测到有效人物，请确保视频中包含清晰的人物");
        }

        // 取存活帧数最长的 Track
        var bestTrack = tracks[0];
        if (bestTrack.FeaturePack is null)
        {
            return Results.BadRequest("特征提取失败");
        }

        // 注册到 Gallery
        var memberId = await familyProvider.EnrollAsync(
            groupId, memberName, bestTrack.FeaturePack, cancellationToken);

        logger.LogInformation("成员 {Name} 注册成功，ID={Id}，Group={Group}",
            memberName, memberId, groupId);

        return Results.Ok(new EnrollResult(memberId, memberName, groupId));
    }
}
