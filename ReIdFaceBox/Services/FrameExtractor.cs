using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReIdFaceBox.Services;

public class FrameExtractor
{
    /// <summary>
    /// 使用 ffmpeg 从视频抽帧，所有帧图片直接输出到内存（不落盘）。
    /// ffmpeg 通过 image2pipe 将 JPEG 帧写入 stdout，此方法按 JPEG SOI/EOI 标记拆分。
    /// </summary>
    public static async Task<List<byte[]>?> ExtractToMemoryAsync(string ffmpegPath, string ffmpegArgs, string videoPath, CancellationToken ct = default)
    {
        if (!File.Exists(ffmpegPath))
            return null;

        // 将用户参数中的 {input} 替换为视频路径，{output} 部分替换为 pipe 输出
        var args = ffmpegArgs
            .Replace("{input}", videoPath)
            .Replace("{output}\\frame_%04d.jpg", "pipe:1")
            .Replace("{output}/frame_%04d.jpg", "pipe:1")
            .Replace("{output}", "pipe:1");

        // 确保输出格式为 image2pipe + mjpeg
        if (!args.Contains("image2pipe"))
        {
            args = args.Replace("pipe:1", "-f image2pipe -vcodec mjpeg pipe:1");
        }

        using var process = new Process();
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

        // 必须异步消费 stderr，否则 ffmpeg 写满 stderr 缓冲区后会阻塞，导致死锁
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        // 从 stdout 读取并拆分 JPEG 帧
        var frames = await SplitJpegFramesAsync(process.StandardOutput.BaseStream, ct);

        // 等待 stderr 读完和进程退出
        await stderrTask;
        await process.WaitForExitAsync(ct);

        return process.ExitCode == 0 || frames.Count > 0 ? frames : null;
    }

    /// <summary>
    /// 从流中按 JPEG SOI (FF D8) 和 EOI (FF D9) 标记拆分出各帧
    /// </summary>
    private static async Task<List<byte[]>> SplitJpegFramesAsync(Stream stream, CancellationToken ct)
    {
        var frames = new List<byte[]>();
        var buffer = new byte[64 * 1024];
        var current = new MemoryStream();
        bool inFrame = false;
        int prev = -1;

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            for (int i = 0; i < bytesRead; i++)
            {
                byte b = buffer[i];

                if (!inFrame)
                {
                    // 寻找 JPEG SOI 标记: FF D8
                    if (prev == 0xFF && b == 0xD8)
                    {
                        inFrame = true;
                        current.SetLength(0);
                        current.WriteByte(0xFF);
                        current.WriteByte(0xD8);
                    }
                }
                else
                {
                    current.WriteByte(b);

                    // 检测 JPEG EOI 标记: FF D9
                    if (prev == 0xFF && b == 0xD9)
                    {
                        frames.Add(current.ToArray());
                        inFrame = false;
                    }
                }

                prev = b;
            }
        }

        // 如果流结束时还有未完成的帧数据，也保存
        if (inFrame && current.Length > 2)
        {
            frames.Add(current.ToArray());
        }

        return frames;
    }
}
