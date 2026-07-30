using ReidFeature.Payloads;

namespace ReidFeature.Services
{
    /// <summary>
    /// 家庭成员提供者接口 — 支持家人注册、删除、列出和多维特征查询
    /// </summary>
    public interface IFamilyMemberProvider
    {
        /// <summary>
        /// 获取指定分组下的所有成员信息
        /// </summary>
        Task<Person[]> GetMembersAsync(string groupId, CancellationToken cancellationToken);

        /// <summary>
        /// 注册新成员 — 将四维特征包存入 Gallery（EMA 初始化）
        /// </summary>
        /// <param name="groupId">分组 ID</param>
        /// <param name="name">成员名称</param>
        /// <param name="featurePack">四维特征包</param>
        /// <returns>成员 ID</returns>
        Task<string> EnrollAsync(string groupId, string name, TrackFeaturePack featurePack, CancellationToken cancellationToken);

        /// <summary>
        /// 删除指定成员
        /// </summary>
        Task<bool> DeleteAsync(string groupId, string memberId, CancellationToken cancellationToken);

        /// <summary>
        /// 列出指定分组的成员信息
        /// </summary>
        Task<MemberInfo[]> ListAsync(string groupId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 成员摘要信息（供列表端点使用）
    /// </summary>
    public sealed record MemberInfo(string Id, string Name, DateTime EnrolledAt);
}
