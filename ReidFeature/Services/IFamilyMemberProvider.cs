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
        /// <param name="append">true 时始终新增一条成员记录（同一成员可注册多条视频，各自独立成条目）；false 时按名称合并更新（默认）</param>
        /// <param name="cancellationToken">取消标记</param>
        /// <returns>成员 ID</returns>
        Task<string> EnrollAsync(string groupId, string name, TrackFeaturePack featurePack, bool append, CancellationToken cancellationToken);

        /// <summary>
        /// 删除指定成员
        /// </summary>
        /// <param name="groupId">分组 ID</param>
        /// <param name="memberId">成员 ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>删除成功返回 true；成员或分组不存在时返回 false</returns>
        Task<bool> DeleteAsync(string groupId, string memberId, CancellationToken cancellationToken);

        /// <summary>
        /// 合并成员 —— 将多个成员的旧特征按等权融合进目标成员，并删除被合并的成员（去重）
        /// </summary>
        /// <param name="groupId">分组 ID</param>
        /// <param name="targetMemberId">保留的目标成员 ID（融合后的主体，不会被删除）</param>
        /// <param name="mergeMemberIds">待合并进目标成员的成员 ID 列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>合并后的成员摘要列表</returns>
        Task<MemberInfo[]> MergeMembersAsync(
            string groupId,
            string targetMemberId,
            IReadOnlyList<string> mergeMemberIds,
            CancellationToken cancellationToken);

        /// <summary>
        /// 列出指定分组的成员信息
        /// </summary>
        /// <param name="groupId">分组 ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>成员摘要信息数组；分组不存在时返回空数组</returns>
        Task<MemberInfo[]> ListAsync(string groupId, CancellationToken cancellationToken);
    }
}
