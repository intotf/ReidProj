namespace ReidFeature.Helpers;

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

    [LoggerMessage(Level = LogLevel.Information, Message = "ReID 特征: dim={Dim}, 耗时 {Elapsed:F1}ms")]
    public static partial void ReIdFeatureExtracted(ILogger logger, long dim, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "YOLO 检测: {Cnt} 人, 耗时 {Elapsed:F1}ms")]
    public static partial void YoloDetectionCompleted(ILogger logger, int cnt, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "视频帧 #{FrameNo} 解码完成, 格式: {Codec}, 累计耗时 {Elapsed:F1}ms")]
    public static partial void VideoDecodeCompleted(ILogger logger, string codec, int frameNo, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "视频全部解码完成, 共 {TotalFrames} 帧, 格式: {Codec}, 总耗时 {Elapsed:F1}ms")]
    public static partial void VideoDecodeAllCompleted(ILogger logger, int totalFrames, string codec, double elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "检测管线异常")]
    public static partial void DetectPipelineFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "姿态估计: 17 点, 耗时 {Elapsed:F1}ms")]
    public static partial void PoseEstimated(ILogger logger, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Track 融合: Track#{TrackId}, {FrameCount} 帧, 耗时 {Elapsed:F1}ms")]
    public static partial void TrackFusionCompleted(ILogger logger, int trackId, int frameCount, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Gallery 注册: 成员 {MemberName}, group={GroupId}")]
    public static partial void GalleryMemberEnrolled(ILogger logger, string memberName, string groupId);

    [LoggerMessage(Level = LogLevel.Information, Message = "识别结果: {MemberName}, score={Score:F3}, track={TrackId}")]
    public static partial void RecognitionResult(ILogger logger, string memberName, float score, int trackId);
}
