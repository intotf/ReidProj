namespace ReIdCli;

public class AppConfig
{
    public string ReidServiceUrl { get; set; } = "http://localhost:9000";
    public string FfmpegPath { get; set; } = @"G:\Tools\ffmpeg\ffmpeg.exe";
    public float SimilarityThreshold { get; set; } = 0.93f;
    public int FrameWidth { get; set; } = 640;
    public int FrameHeight { get; set; } = 360;
    public int FrameRate { get; set; } = 1; // 每秒抽1帧

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mkv", ".mov", ".flv", ".wmv", ".webm", ".ts"
    };
}
