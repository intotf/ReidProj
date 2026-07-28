namespace ReidFeature.Services
{
    /// <summary>
    /// 人物识别结果
    /// </summary>
    /// <param name="Id">人物 ID</param>
    /// <param name="GroupId">所在分组 ID</param>
    /// <param name="Name">人物名称</param>
    /// <param name="ReidSimilarity">人物特征相似度</param>
    /// <param name="SourceFile">匹配来源图片文件名</param>
    public sealed record class PersonRecognition(string Id, string GroupId, string Name, float ReidSimilarity, string? SourceFile);
}
