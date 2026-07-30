namespace ReidFeature.Payloads;

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

    /// <summary>视频帧首次检测到目标后立即停止处理后续帧（仅支持流式视频端点）</summary>
    StopOnFirstFrameHit = 0x2,

    /// <summary>对 ReID 使用灰度图，降低衣服颜色敏感度</summary>
    UseGrayscaleReId = 0x4,
}
