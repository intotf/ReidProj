namespace ReIdMp4Cli;

/// <summary>
/// 应用程序配置，对应 appsettings.json 的 AppConfig 节
/// </summary>
public sealed class AppConfig
{
    /// <summary>ffmpeg 可执行文件路径</summary>
    public string FfmpegPath { get; set; } = "";

    /// <summary>默认分组 ID</summary>
    public string DefaultGroupId { get; set; } = "group2";

    /// <summary>ReidFeature 服务地址</summary>
    public string ServerUrl { get; set; } = "http://localhost:9000";

    /// <summary>相似度阈值</summary>
    public float Threshold { get; set; } = 0.9f;

    /// <summary>检测标志位: 0=All, 1=SkipFaceDetection, 2=StopOnFirstFrameHit</summary>
    public int Flags { get; set; }

    /// <summary>
    /// ffmpeg 抽帧参数模板。支持占位符:
    /// {inputVideo} — 输入视频路径
    /// {outputPattern} — 输出帧图片路径模板
    /// </summary>
    public string FfmpegArgs { get; set; } = "-i \"{inputVideo}\" -vf fps=1 -q:v 2 \"{outputPattern}\"";
}
