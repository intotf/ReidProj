using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyDiscern.Services;

public enum VideoCodec
{
    H264,
    H265,
    Unknown
}

/// <summary>
/// ffmpeg 裸流进程 — 持有 Process 生命周期，通过 OutputStream 获取裸流
/// </summary>
public sealed class FfmpegStreamProcess : IDisposable
{
    private readonly Process _process;

    /// <summary>
    /// ffmpeg stdout 裸流，可直接作为 HTTP body 流式发送
    /// </summary>
    public Stream OutputStream => _process.StandardOutput.BaseStream;

    internal FfmpegStreamProcess(Process process)
    {
        _process = process;
        // 异步消费 stderr 防死锁（fire-and-forget）
        _ = Task.Run(() => { try { _process.StandardError.ReadToEnd(); } catch { } });
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill();
        }
        catch { }
        _process.Dispose();
    }
}

public static class FfmpegService
{
    /// <summary>
    /// 探测 MP4 文件的视频编码格式
    /// </summary>
    public static async Task<VideoCodec> DetectCodecAsync(string ffmpegPath, string mp4Path, CancellationToken ct = default)
    {
        var ffprobePath = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? "", "ffprobe.exe");
        if (!File.Exists(ffprobePath))
            ffprobePath = "ffprobe.exe";

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            Arguments = $"-v error -select_streams v:0 -show_entries stream=codec_name -of csv=p=0 \"{mp4Path}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var codec = output.Trim().ToLowerInvariant();
        return codec switch
        {
            "h264" or "h.264" or "avc" => VideoCodec.H264,
            "hevc" or "h265" or "h.265" => VideoCodec.H265,
            _ => VideoCodec.Unknown
        };
    }

    /// <summary>
    /// 启动 ffmpeg 将 MP4 转为裸流进程，返回流式进程对象。
    /// 调用方通过 OutputStream 直接获取 stdout 裸流数据（不缓存到内存）。
    /// 用完后 Dispose 会终止进程。
    /// </summary>
    public static FfmpegStreamProcess? StartRawStream(string ffmpegPath, string mp4Path, VideoCodec codec)
    {
        if (!File.Exists(ffmpegPath))
            return null;

        var (bsf, format) = codec switch
        {
            VideoCodec.H264 => ("h264_mp4toannexb", "h264"),
            VideoCodec.H265 => ("hevc_mp4toannexb", "hevc"),
            _ => throw new ArgumentException($"不支持的编码: {codec}")
        };

        var args = $"-i \"{mp4Path}\" -c:v copy -bsf:v {bsf} -f {format} pipe:1";

        var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        process.Start();
        return new FfmpegStreamProcess(process);
    }

    /// <summary>
    /// 将 MP4 转换为 Annex-B 裸流临时文件（用于批量注册等需要文件上传的场景）。
    /// 返回临时文件路径，调用方负责在结束后删除；失败返回 null。
    /// </summary>
    /// <param name="ffmpegPath">ffmpeg 可执行文件路径</param>
    /// <param name="mp4Path">MP4 文件路径</param>
    /// <param name="codec">探测到的视频编码</param>
    /// <param name="outputDir">临时文件目录（默认 %TEMP%\familydiscern）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>裸流临时文件路径；失败返回 null</returns>
    public static async Task<string?> ConvertToRawFileAsync(
        string ffmpegPath,
        string mp4Path,
        VideoCodec codec,
        string? outputDir = null,
        CancellationToken ct = default)
    {
        var dir = outputDir ?? Path.Combine(Path.GetTempPath(), "familydiscern");
        Directory.CreateDirectory(dir);

        var ext = codec == VideoCodec.H265 ? "hevc" : "h264";
        var outputPath = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(mp4Path)}_{Guid.NewGuid():N}.{ext}");

        using var process = StartRawStream(ffmpegPath, mp4Path, codec);
        if (process == null)
        {
            return null;
        }

        try
        {
            await using var file = File.Create(outputPath);
            await process.OutputStream.CopyToAsync(file, ct);
            await file.FlushAsync(ct);
            return outputPath;
        }
        catch
        {
            try
            {
                File.Delete(outputPath);
            }
            catch
            {
                // 忽略清理失败
            }
            return null;
        }
    }
}
