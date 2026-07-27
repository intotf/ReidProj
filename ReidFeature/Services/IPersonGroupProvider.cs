using ReidFeature.Payloads;

namespace ReidFeature.Services
{
    /// <summary>
    /// 人物分组提供者接口 — 用于实现人物分组功能的服务接口
    /// </summary>
    public interface IPersonGroupProvider
    {
        /// <summary>
        /// 获取指定分组下的所有人物信息
        /// </summary>
        /// <param name="groupId">分组 ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>指定分组下的所有人物信息</returns>
        Task<Person[]> GetPersonsAsync(string groupId, CancellationToken cancellationToken);
    }
}
