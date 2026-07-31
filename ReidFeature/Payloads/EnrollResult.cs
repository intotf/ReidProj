namespace ReidFeature.Payloads
{
    /// <summary>
    /// 注册结果
    /// </summary>
    /// <param name="MemberId">成员 ID</param>
    /// <param name="Name">成员名称</param>
    /// <param name="GroupId">分组 ID</param>
    public sealed record class EnrollResult(string MemberId, string Name, string GroupId);
}
