namespace ReidFeature.Payloads;

/// <summary>
/// VideoCodec 扩展方法，AOT 安全的名称转换
/// </summary>
static class VideoCodecExtensions
{
    /// <summary>返回 ffmpeg -f 参数所需的格式名称（小写）</summary>
    public static string ToFfmpegFormat(this VideoCodec codec)
    {
        return codec switch
        {
            VideoCodec.H264 => "h264",
            VideoCodec.H265 => "hevc",
            _ => throw new ArgumentOutOfRangeException(nameof(codec))
        };
    }
}
