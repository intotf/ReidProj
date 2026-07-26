using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ReidFeature.Helpers;

/// <summary>
/// 视频解码器 — 通过自带的 ffmpeg 二进制 pipe 流式解码 H264/H265 裸流中的全部帧，文件不落地
/// </summary>
static class VideoDecoder
{
    /// <summary>
    /// 从视频裸流中流式解码所有帧，逐帧返回 RGB 图像
    /// </summary>
    /// <param name="videoStream">H264 或 H265 裸流数据流</param>
    /// <param name="codec">视频编码格式（H264 / H265）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧），如 5 表示每 5 秒一帧</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解码后的 RGB 图像流，无更多帧时结束</returns>
    /// <exception cref="InvalidDataException">视频流数据不完整或格式异常</exception>
    public static async IAsyncEnumerable<Image<Rgb24>> DecodeFramesAsync(
        Stream videoStream,
        VideoCodec codec,
        ILogger logger,
        int frameIntervalSeconds,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        int frameCount = 0;

        using var process = StartFfmpegProcess(codec, frameIntervalSeconds);
        using var writeTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var writeTask = WriteToStdInputAsync(videoStream, process.StandardInput, writeTokenSource.Token);

        try
        {
            await foreach (var image in ReadBmpFramesAsync(process.StandardOutput.BaseStream, cancellationToken))
            {
                frameCount++;
                Log.VideoDecodeCompleted(logger, codec.ToFfmpegFormat(), frameCount, sw.Elapsed.TotalMilliseconds);
                yield return image;
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(); } catch { }
            }

            writeTokenSource.Cancel();
            await writeTask;
        }

        sw.Stop();
        Log.VideoDecodeAllCompleted(logger, frameCount, codec.ToFfmpegFormat(), sw.Elapsed.TotalMilliseconds);
    }


    /// <summary>
    /// 将视频流 pipe 到 ffmpeg 的 stdin 并关闭
    /// </summary>
    private static async Task WriteToStdInputAsync(Stream videoStream, StreamWriter stdInput, CancellationToken cancellationToken)
    {
        try
        {
            await videoStream.CopyToAsync(stdInput.BaseStream, cancellationToken);
        }
        catch (Exception)
        {
            // 吃掉所有异常
        }
        finally
        {
            try
            {
                stdInput.Close();
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// 启动 ffmpeg 进程（仅启动，不写 stdin）
    /// </summary>
    private static Process StartFfmpegProcess(VideoCodec codec, int frameIntervalSeconds)
    {
        var ffmpegFileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "tools", ffmpegFileName);
        if (!File.Exists(ffmpegPath))
        {
            throw new FileNotFoundException("找不到 ffmpeg，请确认 ffmpeg 已放置在 tools 目录", ffmpegPath);
        }

        var format = codec.ToFfmpegFormat();
        // 帧间隔 → ffmpeg -r 参数（帧率 = 1 / 间隔秒数）
        var fps = 1d / frameIntervalSeconds;
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                ArgumentList =
                {
                    "-f", format,
                    "-i", "pipe:0",
                    "-f", "image2pipe",
                    "-c:v", "bmp",
                    "-r", fps.ToString("F6"),
                    "-y",
                    "pipe:1"
                },
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        return process;
    }

    /// <summary>
    /// 从 ffmpeg stdout 中流式读取所有 BMP 帧并逐帧解码
    /// </summary>
    private static async IAsyncEnumerable<Image<Rgb24>> ReadBmpFramesAsync(
        Stream stdout,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        byte[] header = ArrayPool<byte>.Shared.Rent(54);
        try
        {
            while (true)
            {
                int read = 0;
                while (read < 54)
                {
                    int n = await stdout.ReadAsync(header.AsMemory(read, 54 - read), cancellationToken);
                    if (n <= 0)
                        yield break;
                    read += n;
                }

                int fileSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(2));
                int remaining = fileSize - 54;

                byte[] bmpBytes = ArrayPool<byte>.Shared.Rent(fileSize);
                try
                {
                    header.AsSpan(0, 54).CopyTo(bmpBytes);
                    read = 0;
                    while (read < remaining)
                    {
                        int n = await stdout.ReadAsync(bmpBytes.AsMemory(54 + read, remaining - read), cancellationToken);
                        if (n <= 0) throw new InvalidDataException("视频流数据不完整");
                        read += n;
                    }

                    yield return Image.Load<Rgb24>(bmpBytes.AsSpan(0, fileSize));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(bmpBytes);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }
}
