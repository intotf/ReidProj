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
        /// <param name="groupId">分组 ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>该分组下的成员数组；分组不存在时返回空数组</returns>
        Task<Person[]> GetMembersAsync(string groupId, CancellationToken cancellationToken);

        /// <summary>
        /// 注册新成员 — 将四维特征包存入 Gallery（EMA 初始化）
        /// </summary>
        /// <param name="groupId">分组 ID</param>
        /// <param name="name">成员名称</param>
        /// <param name="featurePack">四维特征包</param>
        /// <param name="cancellationToken">取消标记</param>
        /// <returns>成员 ID</returns>
        Task<string> EnrollAsync(string groupId, string name, TrackFeaturePack featurePack, CancellationToken cancellationToken);

        /// <summary>
        /// 删除指定成员
        /// </summary>
        /// <param name="groupId">分组 ID</param>
        /// <param name="memberId">成员 ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>删除成功返回 true；成员或分组不存在时返回 false</returns>
        Task<bool> DeleteAsync(string groupId, string memberId, CancellationToken cancellationToken);

        /// <summary>
        /// 列出指定分组的成员信息
        /// </summary>
        /// <param name="groupId">分组 ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>成员摘要信息数组；分组不存在时返回空数组</returns>
        Task<MemberInfo[]> ListAsync(string groupId, CancellationToken cancellationToken);
    }
}
