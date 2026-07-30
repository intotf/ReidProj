namespace ReidFeature.Payloads
{
    /// <summary>
    /// 人物识别结果 — 视频流处理后的最终输出
    /// </summary>
    /// <param name="Id">人物 ID（stranger 时为空字符串）</param>
    /// <param name="GroupId">所在分组 ID</param>
    /// <param name="Name">人物名称（stranger 时为 "stranger"）</param>
    /// <param name="Score">四维融合相似度分数</param>
    /// <param name="ReidSimilarity">人物特征相似度（保留兼容）</param>
    public sealed record class PersonRecognition(string Id, string GroupId, string Name, float Score, float ReidSimilarity);

    /// <summary>
    /// 注册结果
    /// </summary>
    /// <param name="MemberId">成员 ID</param>
    /// <param name="Name">成员名称</param>
    /// <param name="GroupId">分组 ID</param>
    public sealed record class EnrollResult(string MemberId, string Name, string GroupId);
}
