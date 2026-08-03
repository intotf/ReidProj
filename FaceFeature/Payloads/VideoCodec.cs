namespace FaceFeature.Payloads;

/// <summary>
/// 视频编码格式，用于 /detect/frame 端点
/// </summary>
public enum VideoCodec
{
    /// <summary>原始 H264 裸流（Annex B）</summary>
    H264,
    /// <summary>原始 H265/HEVC 裸流</summary>
    H265,
}
