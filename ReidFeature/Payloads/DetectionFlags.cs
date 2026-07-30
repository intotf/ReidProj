namespace ReidFeature.Payloads;

/// <summary>
/// 检测功能开关标志（可组合，通过 query 参数 flags 传入）
/// </summary>
[Flags]
public enum DetectionFlags
{
    /// <summary>全部开启</summary>
    All = 0,

    /// <summary>视频帧首次检测到目标后立即停止处理后续帧（仅支持流式视频端点）</summary>
    StopOnFirstFrameHit = 0x1,

    /// <summary>启用 ByteTrack 多目标跟踪</summary>
    EnableTracking = 0x2,

    /// <summary>启用 MoveNet 姿态估计（计算体型标量）</summary>
    EnablePose = 0x4,

    /// <summary>启用头肩区域 ReID 特征提取（换衣鲁棒）</summary>
    EnableHeadShoulderReId = 0x8,
}
