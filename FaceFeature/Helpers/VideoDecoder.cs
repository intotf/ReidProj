using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace FaceFeature.Helpers;

/// <summary>
/// 视频解码器 — 通过自带的 ffmpeg 二进制 pipe 流式解码 H264/H265 裸流，文件不落盘。
///
/// 流程：先嗅探裸流 NAL 头识别编码（H264/H265），再按该编码启动 ffmpeg 解码，
/// 输出 rawvideo(RGB24)（无封装头，RGB 字节序与 ImageSharp Rgb24 一致）；
/// 默认启用硬件解码（-hwaccel auto，无可用硬件时自动回退软解）。
/// </summary>
internal static class VideoDecoder
{
    /// <summary>视频编码格式（内部使用，由 NAL 头嗅探判定）</summary>
    private enum VideoCodec
    {
        H264,
        H265,
    }

    /// <summary>编码嗅探读取的最大字节数（Annex B 流的 SPS/PPS/VPS 都在开头，8KB 足够）</summary>
    private const int SniffBufferSize = 8192;

    /// <summary>等待 ffmpeg 输出分辨率的最长时间</summary>
    private static readonly TimeSpan ResolutionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>解析 ffmpeg 输出流分辨率，例如：Video: rawvideo (RGB[24] / 0x18424752), rgb24, 1920x1080, ...</summary>
    private static readonly Regex OutputResolutionRegex = new(
        @"Video:\s+rawvideo.*?(\d{2,5})x(\d{2,5})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>嗅探结果：识别出的编码（无法识别时为 null）与已读出的字节数</summary>
    private readonly record struct SniffResult(VideoCodec? Codec, int Length);

    /// <summary>
    /// 从视频裸流中流式解码所有帧，逐帧返回 RGB 图像
    /// </summary>
    /// <param name="videoStream">H264 或 H265 裸流数据流</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧），如 5 表示每 5 秒一帧，0.5 表示每 0.5 秒一帧</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解码后的 RGB 图像流，无更多帧时结束</returns>
    public static async IAsyncEnumerable<Image<Rgb24>> DecodeFramesAsync(
        Stream videoStream,
        ILogger logger,
        double frameIntervalSeconds,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        byte[] sniffBuffer = ArrayPool<byte>.Shared.Rent(SniffBufferSize);
        try
        {
            // 1. 嗅探编码：读取流前缀并判定 H264 / H265
            var sniff = await SniffCodecAsync(videoStream, sniffBuffer, cancellationToken);
            if (sniff.Length == 0)
            {
                Log.VideoDecodeFailed(logger, new InvalidDataException("视频流为空"));
                yield break;
            }
            if (sniff.Codec is not { } codec)
            {
                throw new InvalidDataException("无法从裸流识别视频编码，仅支持 H264/H265（Annex B）裸流");
            }

            string format = ToFfmpegFormat(codec);
            Log.VideoCodecDetected(logger, format);

            // 2. 启动 ffmpeg 解码会话（进程、输入写入、stderr 排空与分辨率解析）
            var sw = Stopwatch.StartNew();
            await using var session = new FfmpegSession(codec, frameIntervalSeconds, cancellationToken);
            var (width, height) = await session.OpenAsync(
                videoStream,
                sniffBuffer.AsMemory(0, sniff.Length),
                logger);
            Log.VideoDecodeStarted(logger, format, width, height, frameIntervalSeconds);

            // 3. 逐帧消费输出；融合提前结束时，await using 会异步终止进程并清理资源
            int frameCount = 0;
            await foreach (var image in session.ReadFramesAsync(width, height, cancellationToken))
            {
                frameCount++;
                Log.VideoDecodeCompleted(logger, format, frameCount, sw.Elapsed.TotalMilliseconds);
                yield return image;
            }

            sw.Stop();
            Log.VideoDecodeAllCompleted(logger, frameCount, format, sw.Elapsed.TotalMilliseconds);
        }
        finally
        {
            // 等 ffmpeg 会话销毁（含前缀写入完成）后再归还嗅探缓冲区
            ArrayPool<byte>.Shared.Return(sniffBuffer);
        }
    }

    /// <summary>返回 ffmpeg -f 参数所需的格式名称（小写）</summary>
    private static string ToFfmpegFormat(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264",
        VideoCodec.H265 => "hevc",
        _ => throw new ArgumentOutOfRangeException(nameof(codec))
    };

    // ──── 编码嗅探 ──────────────────────────────────────────────

    /// <summary>
    /// 读取裸流前若干字节并判定 H264 / H265；缓冲区由调用方通过
    /// <see cref="ArrayPool{T}"/> 租用并负责归还，本方法不复制数据。
    /// </summary>
    private static async Task<SniffResult> SniffCodecAsync(
        Stream videoStream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < SniffBufferSize)
        {
            int read = await videoStream.ReadAsync(
                buffer.AsMemory(total, SniffBufferSize - total),
                cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
        }

        return total == 0
            ? new SniffResult(null, 0)
            : new SniffResult(DetectCodec(buffer.AsSpan(0, total)), total);
    }

    /// <summary>
    /// 扫描 Annex B NAL 头，只按关键帧参数集判定编码（H265 VPS/SPS/PPS、H264 SPS/PPS，
    /// 两类参数集的字节编码互不冲突），避免 AUD/SEI/片级 NAL 的歧义；未命中参数集时返回 null。
    /// </summary>
    private static VideoCodec? DetectCodec(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i + 3 < data.Length; i++)
        {
            if (data[i] != 0 || data[i + 1] != 0)
            {
                continue;
            }

            // 定位 Annex B 起始码（00 00 01 / 00 00 00 01）后的 NAL 头
            int nalOffset;
            if (data[i + 2] == 1)
            {
                nalOffset = i + 3;
            }
            else if (data[i + 2] == 0 && data[i + 3] == 1)
            {
                nalOffset = i + 4;
            }
            else
            {
                continue;
            }
            if (nalOffset >= data.Length)
            {
                return null;
            }

            byte header = data[nalOffset];
            int h264Type = header & 0x1F;        // H264: 低 5 位
            int h265Type = (header >> 1) & 0x3F; // H265: 次低 6 位

            // H265 参数集：VPS(32) / SPS(33) / PPS(34)
            if (h265Type is 32 or 33 or 34)
            {
                return VideoCodec.H265;
            }

            // H264 参数集：SPS(7) / PPS(8)
            if (h264Type is 7 or 8)
            {
                return VideoCodec.H264;
            }
        }

        return null;
    }

    // ──── ffmpeg 解码会话 ────────────────────────────────────────

    /// <summary>
    /// 封装一次 ffmpeg 解码的完整生命周期：进程、输入写入、stderr 排空与帧输出读取。
    /// 资源在 <see cref="DisposeAsync"/> 时统一清理（终止进程、取消写入、等待收尾任务）。
    /// </summary>
    private sealed class FfmpegSession : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly CancellationTokenSource _writeCts;
        private Task _writeTask = Task.CompletedTask;
        private Task _stderrTask = Task.CompletedTask;

        public FfmpegSession(VideoCodec codec, double frameIntervalSeconds, CancellationToken cancellationToken)
        {
            _process = CreateFfmpegProcess(codec, frameIntervalSeconds);
            _writeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        /// <summary>
        /// 启动输入写入与 stderr 排空，并等待 ffmpeg 输出流头解析出分辨率
        /// </summary>
        /// <param name="input">剩余视频流（前缀之后的部分）</param>
        /// <param name="prefix">嗅探阶段已读出的前缀字节</param>
        /// <param name="logger">日志记录器</param>
        /// <returns>输出帧的分辨率</returns>
        public async Task<(int Width, int Height)> OpenAsync(Stream input, ReadOnlyMemory<byte> prefix, ILogger logger)
        {
            var resolutionTcs = new TaskCompletionSource<(int Width, int Height)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _stderrTask = DrainStderrAsync(_process.StandardError, resolutionTcs, logger, _writeCts.Token);
            _writeTask = WriteInputAsync(input, prefix, _process.StandardInput, _writeCts.Token);

            return await resolutionTcs.Task.WaitAsync(ResolutionTimeout, _writeCts.Token);
        }

        /// <summary>流式读取解码后的 RGB 帧（每帧固定 width×height×3 字节，无封装头）</summary>
        public async IAsyncEnumerable<Image<Rgb24>> ReadFramesAsync(
            int width,
            int height,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int frameSize = width * height * 3;
            byte[] frame = ArrayPool<byte>.Shared.Rent(frameSize);
            try
            {
                while (true)
                {
                    int read = await _process.StandardOutput.BaseStream.ReadAtLeastAsync(
                        frame.AsMemory(0, frameSize),
                        frameSize,
                        throwOnEndOfStream: false,
                        cancellationToken);

                    if (read < frameSize)
                    {
                        yield break;
                    }

                    yield return Image.LoadPixelData<Rgb24>(frame.AsSpan(0, frameSize), width, height);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame);
            }
        }

        public async ValueTask DisposeAsync()
        {
            // 终止 ffmpeg（未退出时），让写入/排空任务因管道关闭而快速收尾
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch
            {
                // 忽略进程清理异常
            }

            // 取消写入并等待两个收尾任务（其内部异常均已吞掉）
            await _writeCts.CancelAsync();
            await _writeTask;
            await _stderrTask;

            _process.Dispose();
            _writeCts.Dispose();
        }
    }

    /// <summary>启动 ffmpeg 进程（仅启动，不写 stdin）</summary>
    private static Process CreateFfmpegProcess(VideoCodec codec, double frameIntervalSeconds)
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
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // 硬件解码加速，无可用硬件时 ffmpeg 自动回退软解
        startInfo.ArgumentList.Add("-hwaccel");
        startInfo.ArgumentList.Add("auto");

        // 输入：裸流，按嗅探出的编码解析
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(ToFfmpegFormat(codec));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add("pipe:0");

        // 输出：无封装 rawvideo(RGB24)，字节序与 ImageSharp Rgb24 一致
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("rawvideo");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("rgb24");

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

    /// <summary>将嗅探前缀 + 剩余视频流写入 ffmpeg stdin 并关闭</summary>
    private static async Task WriteInputAsync(
        Stream videoStream,
        ReadOnlyMemory<byte> prefix,
        StreamWriter stdInput,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!prefix.IsEmpty)
            {
                await stdInput.BaseStream.WriteAsync(prefix, cancellationToken);
            }
            await videoStream.CopyToAsync(stdInput.BaseStream, cancellationToken);
        }
        catch (Exception)
        {
            // 吃掉所有异常（进程被杀 / 流提前结束都属于正常收尾）
        }
        finally
        {
            try
            {
                stdInput.Close();
            }
            catch (Exception)
            {
                // 忽略关闭异常
            }
        }
    }

    /// <summary>
    /// 持续排空 ffmpeg stderr（防止管道缓冲写满阻塞 ffmpeg），并解析输出流分辨率；
    /// 解码失败时把 ffmpeg 尾部日志带进异常，避免空等超时。
    /// </summary>
    private static async Task DrainStderrAsync(
        StreamReader stderr,
        TaskCompletionSource<(int Width, int Height)> resolution,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var tail = new Queue<string>(8);
        try
        {
            while (true)
            {
                string? line = await stderr.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (tail.Count == 8)
                {
                    tail.Dequeue();
                }
                tail.Enqueue(line);

                if (!resolution.Task.IsCompleted)
                {
                    var match = OutputResolutionRegex.Match(line);
                    if (match.Success)
                    {
                        resolution.TrySetResult((
                            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 调用方已取消
        }
        catch (IOException)
        {
            // stderr 提前关闭，忽略
        }
        finally
        {
            if (!resolution.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                var reason = tail.Count == 0
                    ? "ffmpeg 未输出任何日志"
                    : string.Join(" | ", tail);
                Log.VideoDecodeError(logger, reason);
                resolution.TrySetException(new IOException($"ffmpeg 解码失败，无法解析输出分辨率：{reason}"));
            }
        }
    }
}
