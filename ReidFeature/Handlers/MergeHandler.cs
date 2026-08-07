using Microsoft.AspNetCore.Mvc;
using ReidFeature.Payloads;
using ReidFeature.Services;

namespace ReidFeature.Handlers;

/// <summary>
/// 成员合并（去重）处理器 —— 将同一人的多条 Gallery 记录合并为一条
/// </summary>
public static class MergeHandler
{
    /// <summary>
    /// 处理成员合并请求
    /// </summary>
    /// <param name="groupId">分组 ID</param>
    /// <param name="request">合并请求（目标成员 + 待合并成员 ID 列表）</param>
    /// <param name="provider">家庭成员提供者</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>合并后的成员摘要列表</returns>
    public static async Task<IResult> HandleMergeAsync(
        string groupId,
        [FromBody] MergeMembersRequest request,
        IFamilyMemberProvider provider,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TargetMemberId))
        {
            return Results.BadRequest("targetMemberId 不能为空");
        }

        var members = await provider.MergeMembersAsync(
            groupId,
            request.TargetMemberId,
            request.MergeMemberIds ?? [],
            cancellationToken);
        return Results.Ok(members);
    }
}
