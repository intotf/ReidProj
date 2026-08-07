namespace ReidFeature.Payloads;

/// <summary>
/// 成员合并（去重）请求
/// </summary>
/// <param name="TargetMemberId">保留的目标成员 ID（融合后的主体，不会被删除）</param>
/// <param name="MergeMemberIds">待合并进目标成员的成员 ID 列表（合并后删除）</param>
public sealed record class MergeMembersRequest(string TargetMemberId, string[] MergeMemberIds);
