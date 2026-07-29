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

    [LoggerMessage(Level = LogLevel.Information, Message = "视频全部解码完成, 共 {TotalFrames} 帧, 格式: {Codec}, 总耗时 {Elapsed:F1}ms")]
    public static partial void VideoDecodeAllCompleted(ILogger logger, int totalFrames, string codec, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "人脸特征: dim={Dim}, 耗时 {Elapsed:F1}ms")]
    public static partial void FaceFeatureExtracted(ILogger logger, long dim, double elapsed);
}
