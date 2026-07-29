using FaceFeature.Payloads;

namespace FaceFeature.Services
{
    /// <summary>
    /// 人脸分组提供者接口 — 用于实现人脸分组识别功能的服务接口
    /// </summary>
    public interface IFaceGroupProvider
    {
        /// <summary>
        /// 获取指定分组下的所有人脸人物信息
        /// </summary>
        /// <param name="groupId">分组 ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>指定分组下的所有人脸人物信息</returns>
        Task<FacePerson[]> GetPersonsAsync(string groupId, CancellationToken cancellationToken);
    }
}
