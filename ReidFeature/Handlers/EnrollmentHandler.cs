using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;

namespace ReidFeature.Handlers;

/// <summary>
/// 家庭成员注册处理器
/// 上传 H264/H265 视频流（编码自动识别）→ 检测 → 跟踪 → 特征融合 → 存入 Gallery
/// </summary>
public static class EnrollmentHandler
{
    /// <summary>
    /// 处理视频流注册（H264/H265 裸流均可，编码自动识别）
    /// </summary>
    /// <param name="familyProvider">家庭成员提供者（Gallery 数据源）</param>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="detectService">检测编排服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="groupId">分组 ID</param>
    /// <param name="memberName">成员名称</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧），如 0.5 表示每 0.5 秒一帧；≤0 时解码全部帧</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注册结果（成员 ID、名称、分组）；请求体为空、memberName 为空、未检测到人物或特征提取失败时返回 BadRequest</returns>
    public static async Task<IResult> HandleEnrollAsync(
        IFamilyMemberProvider familyProvider,
        HttpContext context,
        DetectService detectService,
        ILogger<Program> logger,
        string groupId,
        string memberName,
        double frameIntervalSeconds = 0.5,
        CancellationToken cancellationToken = default)
    {
        var request = context.Request;

        // 防御无效参数：NaN/±Infinity 统一按 0（解码全部帧）处理，并 clamp 到合理上限
        if (double.IsNaN(frameIntervalSeconds) || double.IsInfinity(frameIntervalSeconds))
        {
            frameIntervalSeconds = 0;
        }
        frameIntervalSeconds = Math.Clamp(frameIntervalSeconds, 0, 3600);

        if (string.IsNullOrWhiteSpace(memberName))
        {
            return Results.BadRequest("memberName 不能为空");
        }

        // 处理视频流（解码 → 逐帧检测/跟踪/缓存，统一由 DetectService 处理）
        if (!await detectService.ProcessVideoStreamAsync(
            request, logger, frameIntervalSeconds, cancellationToken))
        {
            return Results.BadRequest("视频解码失败");
        }

        // 获取完成的 Track（取主 Track）
        var tracks = detectService.FlushCompletedTracks();
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

        Log.MemberEnrolled(logger, memberName, memberId, groupId);

        return Results.Ok(new EnrollResult(memberId, memberName, groupId));
    }
}
