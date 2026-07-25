using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;

namespace ReidFeature.Helpers;

/// <summary>
/// 视频解码器 — 通过自带的 ffmpeg 二进制 pipe 解码单帧 H264/H265 裸流，文件不落地
/// </summary>
static class VideoDecoder
{
    /// <summary>
    /// 从视频裸流中解码单帧，返回 RGB 图像
    /// </summary>
    /// <param name="videoStream">H264 或 H265 裸流数据流</param>
    /// <param name="codec">视频编码格式（H264 / H265）</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>解码后的 RGB 图像</returns>
    /// <exception cref="InvalidDataException">视频流数据不完整或格式异常</exception>
    public static async Task<Image<Rgb24>> DecodeSingleFrameAsync(Stream videoStream, VideoCodec codec, ILogger logger)
    {
        var sw = Stopwatch.StartNew();

        using var process = await StartFfmpegProcessAsync(videoStream, codec);
        var image = await ReadBmpFromStreamAsync(process.StandardOutput.BaseStream);

        sw.Stop();
        Log.VideoDecodeCompleted(logger, codec.ToFfmpegFormat(), sw.Elapsed.TotalMilliseconds);

        return image;
    }

    /// <summary>
    /// 启动 ffmpeg 进程并将视频流 pipe 到其 stdin
    /// </summary>
    private static async Task<Process> StartFfmpegProcessAsync(Stream videoStream, VideoCodec codec)
    {
        var ffmpegFileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "tools", ffmpegFileName);
        if (!File.Exists(ffmpegPath))
        {
            throw new FileNotFoundException("找不到 ffmpeg，请确认 ffmpeg 已放置在 tools 目录", ffmpegPath);
        }

        var format = codec.ToFfmpegFormat();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                ArgumentList =
                {
                    "-f", format,
                    "-i", "pipe:0",
                    "-frames:v", "1",
                    "-f", "image2pipe",
                    "-c:v", "bmp",
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

        await videoStream.CopyToAsync(process.StandardInput.BaseStream);
        process.StandardInput.Close();

        return process;
    }

    /// <summary>
    /// 从 ffmpeg stdout 中读取 BMP 数据并直接解码为图像
    /// </summary>
    private static async Task<Image<Rgb24>> ReadBmpFromStreamAsync(Stream stdout)
    {
        // BMP header 至少 54 字节
        byte[] header = ArrayPool<byte>.Shared.Rent(54);
        try
        {
            int read = 0;
            while (read < 54)
            {
                int n = await stdout.ReadAsync(header.AsMemory(read, 54 - read));
                if (n <= 0) throw new InvalidDataException("视频流数据不完整");
                read += n;
            }

            int fileSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(2));
            int remaining = fileSize - 54;

            // 从池中申请完整 BMP 空间直接交给 Image.Load
            byte[] bmpBytes = ArrayPool<byte>.Shared.Rent(fileSize);
            try
            {
                header.CopyTo(bmpBytes, 0);
                read = 0;
                while (read < remaining)
                {
                    int n = await stdout.ReadAsync(bmpBytes.AsMemory(54 + read, remaining - read));
                    if (n <= 0) throw new InvalidDataException("视频流数据不完整");
                    read += n;
                }

                // Image.Load 会复制数据，池可安心回收
                return Image.Load<Rgb24>(bmpBytes.AsSpan(0, fileSize));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bmpBytes);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }
}
