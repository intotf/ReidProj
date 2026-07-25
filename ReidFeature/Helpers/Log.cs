namespace ReidFeature.Helpers;

/// <summary>
/// 集中定义结构化日志消息模板（LoggerMessage 源生成器模式）
/// </summary>
static partial class Log
{
    // ===== DetectHandler =====
    [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "请求体为空")]
    public static partial void RequestBodyEmpty(ILogger logger);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "收到图片请求: {Len} bytes")]
    public static partial void ImageRequestReceived(ILogger logger, long len);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "图片解码失败")]
    public static partial void ImageDecodeFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "检测完成: {Cnt} 个人物, 耗时 {Elapsed:F1}ms")]
    public static partial void DetectionCompleted(ILogger logger, int cnt, double elapsed);

    // ===== ReIdExtractor =====
    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "加载 ReID 模型: {Path}")]
    public static partial void LoadingReIdModel(ILogger logger, string path);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "ReID 模型加载完成")]
    public static partial void ReIdModelLoaded(ILogger logger);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "ReID 特征: dim={Dim}, 耗时 {Elapsed:F1}ms")]
    public static partial void ReIdFeatureExtracted(ILogger logger, long dim, double elapsed);

    // ===== YoloDetector =====
    [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "加载 YOLO 模型: {Path}")]
    public static partial void LoadingYoloModel(ILogger logger, string path);

    [LoggerMessage(EventId = 21, Level = LogLevel.Information, Message = "YOLO 模型加载完成, 输入: {Cnt}")]
    public static partial void YoloModelLoaded(ILogger logger, int cnt);

    [LoggerMessage(EventId = 22, Level = LogLevel.Information, Message = "YOLO 检测: {Cnt} 人, 耗时 {Elapsed:F1}ms")]
    public static partial void YoloDetectionCompleted(ILogger logger, int cnt, double elapsed);

    // ===== FaceDetector =====
    [LoggerMessage(EventId = 30, Level = LogLevel.Information, Message = "加载人脸检测模型: {Path}")]
    public static partial void LoadingFaceModel(ILogger logger, string path);

    [LoggerMessage(EventId = 31, Level = LogLevel.Information, Message = "人脸检测模型加载完成")]
    public static partial void FaceModelLoaded(ILogger logger);

    [LoggerMessage(EventId = 32, Level = LogLevel.Information, Message = "人脸检测: {Cnt} 个, 耗时 {Elapsed:F1}ms")]
    public static partial void FaceDetectionCompleted(ILogger logger, int cnt, double elapsed);

    [LoggerMessage(EventId = 33, Level = LogLevel.Information, Message = "最佳人脸: score={Score:F3}, 耗时 {Elapsed:F1}ms")]
    public static partial void BestFaceDetected(ILogger logger, float score, double elapsed);

    [LoggerMessage(EventId = 34, Level = LogLevel.Debug, Message = "未检测到最佳人脸, 耗时 {Elapsed:F1}ms")]
    public static partial void BestFaceNotFound(ILogger logger, double elapsed);

    // ===== VideoDecoder =====
    [LoggerMessage(EventId = 40, Level = LogLevel.Information, Message = "视频解码完成, 编码格式: {Codec}, 耗时 {Elapsed:F1}ms")]
    public static partial void VideoDecodeCompleted(ILogger logger, string codec, double elapsed);
}
