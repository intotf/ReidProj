using System.Diagnostics;

namespace ReIdCli;

/// <summary>
/// 使用 ffmpeg 从视频中抽帧
/// </summary>
public class FrameExtractor
{
    private readonly string _ffmpegPath;

    public FrameExtractor(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;

        if (!File.Exists(_ffmpegPath))
        {
            throw new FileNotFoundException($"ffmpeg 不存在: {_ffmpegPath}");
        }
    }

    /// <summary>
    /// 从视频中每秒抽1帧，输出为 640x360 的 jpg 图片
    /// </summary>
    /// <param name="videoPath">视频文件路径</param>
    /// <param name="outputDir">输出目录</param>
    /// <returns>是否成功</returns>
    public async Task<bool> ExtractFramesAsync(string videoPath, string outputDir)
    {
        var outputPattern = Path.Combine(outputDir, "frame_%04d.jpg");

        // ffmpeg -i input.mp4 -vf "fps=1,scale=640:360" -q:v 2 output/frame_%04d.jpg
        var arguments = $"-i \"{videoPath}\" -vf \"fps=1,scale=640:360\" -q:v 2 \"{outputPattern}\" -y";

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return false;

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
}
