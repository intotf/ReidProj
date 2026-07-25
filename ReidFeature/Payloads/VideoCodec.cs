namespace ReidFeature.Payloads;

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

/// <summary>
/// VideoCodec 扩展方法，AOT 安全的名称转换
/// </summary>
static class VideoCodecExtensions
{ 
    /// <summary>返回 ffmpeg -f 参数所需的格式名称（小写）</summary>
    public static string ToFfmpegFormat(this VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264",
        VideoCodec.H265 => "hevc",
        _ => throw new ArgumentOutOfRangeException(nameof(codec))
    };
}
