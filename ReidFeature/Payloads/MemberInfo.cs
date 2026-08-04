namespace ReidFeature.Payloads;

/// <summary>
/// 成员摘要信息（供列表端点使用）
/// </summary>
public sealed record MemberInfo(string Id, string Name, DateTime EnrolledAt);
