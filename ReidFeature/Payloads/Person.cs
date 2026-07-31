namespace ReidFeature.Payloads
{
    /// <summary>
    /// 人物信息
    /// </summary>
    /// <remarks>
    /// 人物信息
    /// </remarks>
    /// <param name="id">人物 ID</param>
    /// <param name="groupId">所在分组 ID</param>
    /// <param name="name">人物名称</param>
    public sealed class Person(string id, string groupId, string name)
    {
        /// <summary>
        /// 人物 ID
        /// </summary>
        public string Id { get; set; } = id;
        /// <summary>
        /// 所在分组 ID
        /// </summary>
        public string GroupId { get; set; } = groupId;
        /// <summary>
        /// 人物名称
        /// </summary>
        public string Name { get; set; } = name;

        /// <summary>
        /// 四维特征包（全身 ReID + 头肩 ReID + 体型标量 + 步态标量）
        /// </summary>
        public TrackFeaturePack? FeaturePack { get; set; }
    }
}
