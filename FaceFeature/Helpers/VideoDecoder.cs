using FaceFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FaceFeature.Helpers;

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
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧），如 5 表示每 5 秒一帧，0.5 表示每 0.5 秒一帧</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解码后的 RGB 图像流，无更多帧时结束</returns>
    /// <exception cref="InvalidDataException">视频流数据不完整或格式异常</exception>
    public static async IAsyncEnumerable<Image<Rgb24>> DecodeFramesAsync(
        Stream videoStream,
        VideoCodec codec,
        ILogger logger,
        double frameIntervalSeconds,
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
    private static Process StartFfmpegProcess(VideoCodec codec, double frameIntervalSeconds)
    {
        var ffmpegFileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "tools", ffmpegFileName);
        if (!File.Exists(ffmpegPath))
        {
            throw new FileNotFoundException("找不到 ffmpeg，请确认 ffmpeg 已放置在 tools 目录", ffmpegPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(codec.ToFfmpegFormat());
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add("pipe:0");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("image2pipe");
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("bmp");

        if (frameIntervalSeconds > 0)
        {
            startInfo.ArgumentList.Add("-r");
            startInfo.ArgumentList.Add((1d / frameIntervalSeconds).ToString("F6"));
        }

        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("pipe:1");

        var process = new Process { StartInfo = startInfo };
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
        const int BmpHeaderSize = 54;
        byte[] header = ArrayPool<byte>.Shared.Rent(BmpHeaderSize);
        try
        {
            while (true)
            {
                int read = await stdout.ReadAtLeastAsync(
                    header.AsMemory(0, BmpHeaderSize),
                    BmpHeaderSize,
                    throwOnEndOfStream: false,
                    cancellationToken);

                if (read < BmpHeaderSize)
                {
                    yield break;
                }

                int fileSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(2));
                int remaining = fileSize - BmpHeaderSize;

                byte[] bmpBytes = ArrayPool<byte>.Shared.Rent(fileSize);
                try
                {
                    header.AsSpan(0, BmpHeaderSize).CopyTo(bmpBytes);
                    await stdout.ReadExactlyAsync(bmpBytes.AsMemory(BmpHeaderSize, remaining), cancellationToken);

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
