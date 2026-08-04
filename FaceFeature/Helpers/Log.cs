namespace FaceFeature.Helpers;

/// <summary>
/// 集中定义结构化日志消息模板（LoggerMessage 源生成器模式）
/// </summary>
static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "请求体为空")]
    public static partial void RequestBodyEmpty(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "图片解码失败")]
    public static partial void ImageDecodeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "视频帧解码失败")]
    public static partial void VideoDecodeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "人脸检测: {Cnt} 个, 耗时 {Elapsed:F1}ms")]
    public static partial void FaceDetectionCompleted(ILogger logger, int cnt, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "最佳人脸: score={Score:F3}, 耗时 {Elapsed:F1}ms")]
    public static partial void BestFaceDetected(ILogger logger, float score, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "未检测到最佳人脸, 耗时 {Elapsed:F1}ms")]
    public static partial void BestFaceNotFound(ILogger logger, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "视频帧 #{FrameNo} 解码完成, 格式: {Codec}, 累计耗时 {Elapsed:F1}ms")]
    public static partial void VideoDecodeCompleted(ILogger logger, string codec, int frameNo, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "视频解码启动: 格式={Codec}, 分辨率={Width}x{Height}, 抽帧间隔={Interval:F1}s")]
    public static partial void VideoDecodeStarted(ILogger logger, string codec, int width, int height, double interval);

    [LoggerMessage(Level = LogLevel.Information, Message = "视频全部解码完成, 共 {TotalFrames} 帧, 格式: {Codec}, 总耗时 {Elapsed:F1}ms")]
    public static partial void VideoDecodeAllCompleted(ILogger logger, int totalFrames, string codec, double elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "ffmpeg 解码失败: {Reason}")]
    public static partial void VideoDecodeError(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "视频编码识别: {Codec}")]
    public static partial void VideoCodecDetected(ILogger logger, string codec);

    [LoggerMessage(Level = LogLevel.Information, Message = "人脸特征: dim={Dim}, 耗时 {Elapsed:F1}ms")]
    public static partial void FaceFeatureExtracted(ILogger logger, long dim, double elapsed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "人脸五点对齐完成, 关键点={Kps}, 耗时 {Elapsed:F1}ms")]
    public static partial void FaceAligned(ILogger logger, int kps, double elapsed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "人脸清晰度: score={Score:F1}")]
    public static partial void FaceSharpness(ILogger logger, float score);

    [LoggerMessage(Level = LogLevel.Information, Message = "跳过模糊帧: sharpness={Score:F1} < threshold={Threshold:F1}")]
    public static partial void FaceSkippedBlurry(ILogger logger, float score, float threshold);

    [LoggerMessage(Level = LogLevel.Warning, Message = "底库注册照质量偏低: {Name}, sharpness={Score:F1}")]
    public static partial void FaceGalleryLowQuality(ILogger logger, string name, float score);

    [LoggerMessage(Level = LogLevel.Warning, Message = "人脸库存储错误: {Message}")]
    public static partial void FaceStoreError(ILogger logger, string message, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "多帧融合完成: 使用 {Frames} 帧, 提前完成={Early}, 相似度={Score:F4}")]
    public static partial void FaceFusionCompleted(ILogger logger, int frames, bool early, float score);

    [LoggerMessage(Level = LogLevel.Debug, Message = "跳过离群帧: 余弦={Cosine:F3} < 门控={Gate:F3}")]
    public static partial void FaceFusionSkippedOutlier(ILogger logger, float cosine, float gate);
}
