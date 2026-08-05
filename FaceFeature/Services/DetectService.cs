using FaceFeature.Helpers;
using FaceFeature.Payloads;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.CompilerServices;

namespace FaceFeature.Services;

/// <summary>
/// 人脸检测编排服务：SCRFD 全图人脸检测 → ArcFace 逐人脸特征提取
/// 不依赖 YOLO 人物检测，直接在全图上检测人脸。
/// </summary>
public sealed class DetectService
{
    private readonly FaceDetector _faceDetector;
    private readonly FaceExtractor _faceExtractor;
    private readonly FaceQualityOptions _qualityOptions;
    private readonly ILogger<DetectService> _logger;

    /// <summary>
    /// 人脸检测编排服务
    /// </summary>
    /// <param name="faceDetector">人脸检测器</param>
    /// <param name="faceExtractor">人脸特征提取器</param>
    /// <param name="qualityOptions">清晰度筛选配置</param>
    /// <param name="logger">日志记录器</param>
    public DetectService(
        FaceDetector faceDetector,
        FaceExtractor faceExtractor,
        IOptions<FaceQualityOptions> qualityOptions,
        ILogger<DetectService> logger)
    {
        _faceDetector = faceDetector;
        _faceExtractor = faceExtractor;
        _qualityOptions = qualityOptions.Value;
        _logger = logger;
    }



    /// <summary>
    /// 视频逐帧检测流：解码 H264/H265 裸流，逐帧检测并跳过模糊帧
    /// </summary>
    /// <remarks>返回的枚举器必须被完整消费或释放，否则池化缓冲与 ffmpeg 进程无法清理。</remarks>
    /// <param name="videoStream">H264/H265 裸流数据流</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async IAsyncEnumerable<FaceDetection> DetectFramesAsync(
        Stream videoStream,
        double frameIntervalSeconds,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var image in VideoDecoder.DecodeFramesAsync(
            videoStream, _logger, frameIntervalSeconds, cancellationToken))
        {
            using (image)
            {
                // 上一帧无人脸时，本帧跳过检测，仅消费解码帧以保持 ffmpeg 管道流通
                if (skipNext)
                {
                    skipNext = false;
                    continue;
                }

                var detection = DetectBestFace(image);
                if (detection is not null)
                {
                    yield return detection;
                }
            }
        }
    }

    /// <summary>
    /// 对输入图像检测置信度最高的最佳人脸（性能优先——避免全量特征提取）
    /// </summary>
    /// <param name="image">输入 RGB 图像</param> 
    /// <returns>最佳人脸检测结果（含清晰度分数），无人脸或模糊被跳过时返回 null</returns>
    public FaceDetection? DetectBestFace(Image<Rgb24> image)
    {
        var best = _faceDetector.DetectBest(image);
        if (best is null)
        {
            return null;
        }

        using var extraction = _faceExtractor.AlignAndScore(image, best);

        if (_options.FaceQuality.Enabled && extraction.Sharpness < _options.FaceQuality.SharpnessThreshold)
        {
            Log.FaceSkippedBlurry(_logger, extraction.Sharpness, _qualityOptions.SharpnessThreshold);
            return null;
        }

        var features = _faceExtractor.ExtractFeatures(extraction.Aligned);

        var box = best.Bbox;
        var faceRect = new Rectangle(
            Math.Clamp(box.X, 0, image.Width - 1),
            Math.Clamp(box.Y, 0, image.Height - 1),
            Math.Max(1, Math.Min(box.Width, image.Width - box.X)),
            Math.Max(1, Math.Min(box.Height, image.Height - box.Y)));

        return new FaceDetection(
            Bbox: faceRect,
            Confidence: best.Confidence,
            Features: features,
            Sharpness: extraction.Sharpness);
    }
}
