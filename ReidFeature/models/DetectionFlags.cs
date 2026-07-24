namespace ReidFeature.Models;

/// <summary>
/// 检测功能开关标志（可组合，通过 query 参数 flags 传入）
/// </summary>
[Flags]
public enum DetectionFlags
{
    /// <summary>全部开启</summary>
    All = 0,

    /// <summary>跳过人脸检测</summary>
    SkipFaceDetection = 0x1,
}
