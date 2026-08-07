namespace ReidFeature.Payloads;

/// <summary>
/// 单个视频段的注册信息（批量注册结果中的明细项）
/// </summary>
/// <param name="FileName">视频文件名</param>
/// <param name="TrackId">采用的最长 Track ID（0 表示该段未检测到有效人物）</param>
public sealed record class EnrollSegmentInfo(string FileName, int TrackId);

/// <summary>
/// 批量注册（同一人多段注册）结果
/// </summary>
/// <param name="MemberId">成员 ID（同名已存在时保持不变，特征融合更新）</param>
/// <param name="Name">成员名称</param>
/// <param name="GroupId">分组 ID</param>
/// <param name="SegmentCount">成功融合的视频段数</param>
/// <param name="Segments">各视频段明细</param>
public sealed record class EnrollBatchResult(
    string MemberId,
    string Name,
    string GroupId,
    int SegmentCount,
    List<EnrollSegmentInfo> Segments);
