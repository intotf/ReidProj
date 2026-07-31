namespace ReidFeature.Payloads
{
    /// <summary>
    /// 人物识别结果 — 视频流处理后的最终输出
    /// </summary>
    /// <param name="Id">人物 ID（stranger 时为空字符串）</param>
    /// <param name="GroupId">所在分组 ID</param>
    /// <param name="Name">人物名称（stranger 时为 "stranger"）</param>
    /// <param name="Score">四维融合相似度分数</param>
    /// <param name="ClothScore">全身 ReID 相似度</param>
    /// <param name="HeadScore">头肩 ReID 相似度</param>
    /// <param name="BodyScore">体型标量相似度</param>
    /// <param name="GaitScore">步态标量相似度</param>
    public sealed record class PersonRecognition(
        string Id,
        string GroupId,
        string Name,
        float Score,
        float ClothScore,
        float HeadScore,
        float BodyScore,
        float GaitScore);
}
