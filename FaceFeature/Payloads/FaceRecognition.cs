namespace FaceFeature.Payloads
{
    /// <summary>
    /// 人脸识别结果
    /// </summary>
    /// <param name="Id">人物 ID</param>
    /// <param name="GroupId">所在分组 ID</param>
    /// <param name="Name">人物名称</param>
    /// <param name="FaceSimilarity">人脸特征相似度</param>
    public sealed record class FaceRecognition(string Id, string GroupId, string Name, float FaceSimilarity);
}
