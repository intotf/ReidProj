namespace ReidFeature.Helpers;

/// <summary>
/// 集中定义结构化日志消息模板（LoggerMessage 源生成器模式）
/// </summary>
static partial class Log
{
    /// <summary>记录请求体为空的警告日志。</summary>
    /// <param name="logger">日志记录器</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "请求体为空")]
    public static partial void RequestBodyEmpty(ILogger logger);

    /// <summary>记录图片解码失败的警告日志。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="exception">解码异常</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "图片解码失败")]
    public static partial void ImageDecodeFailed(ILogger logger, Exception exception);

    /// <summary>记录视频帧解码失败的警告日志。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="exception">解码异常</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "视频帧解码失败")]
    public static partial void VideoDecodeFailed(ILogger logger, Exception exception);

    /// <summary>记录 ReID 特征提取完成信息（维度与耗时）。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="dim">特征维度</param>
    /// <param name="elapsed">耗时（毫秒）</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "ReID 特征: dim={Dim}, 耗时 {Elapsed:F1}ms")]
    public static partial void ReIdFeatureExtracted(ILogger logger, long dim, double elapsed);

    /// <summary>记录 YOLO 检测完成信息（人数与耗时）。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cnt">检测到的人数</param>
    /// <param name="elapsed">耗时（毫秒）</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "YOLO 检测: {Cnt} 人, 耗时 {Elapsed:F1}ms")]
    public static partial void YoloDetectionCompleted(ILogger logger, int cnt, double elapsed);

    /// <summary>记录单帧解码完成信息（帧号、编码格式、累计耗时）。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="codec">编码格式名称</param>
    /// <param name="frameNo">帧号</param>
    /// <param name="elapsed">累计耗时（毫秒）</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "视频帧 #{FrameNo} 解码完成, 格式: {Codec}, 累计耗时 {Elapsed:F1}ms")]
    public static partial void VideoDecodeCompleted(ILogger logger, string codec, int frameNo, double elapsed);

    /// <summary>记录视频全部解码完成信息（总帧数、编码格式、总耗时）。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="totalFrames">总帧数</param>
    /// <param name="codec">编码格式名称</param>
    /// <param name="elapsed">总耗时（毫秒）</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "视频全部解码完成, 共 {TotalFrames} 帧, 格式: {Codec}, 总耗时 {Elapsed:F1}ms")]
    public static partial void VideoDecodeAllCompleted(ILogger logger, int totalFrames, string codec, double elapsed);

    /// <summary>记录检测管线异常。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="exception">管线异常</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "检测管线异常")]
    public static partial void DetectPipelineFailed(ILogger logger, Exception exception);

    /// <summary>记录姿态估计完成信息（17 点与耗时）。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="elapsed">耗时（毫秒）</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "姿态估计: 17 点, 耗时 {Elapsed:F1}ms")]
    public static partial void PoseEstimated(ILogger logger, double elapsed);

    /// <summary>记录 Track 融合完成信息（Track ID、帧数与耗时）。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="trackId">Track ID</param>
    /// <param name="frameCount">参与融合的帧数</param>
    /// <param name="elapsed">耗时（毫秒）</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Track 融合: Track#{TrackId}, {FrameCount} 帧, 耗时 {Elapsed:F1}ms")]
    public static partial void TrackFusionCompleted(ILogger logger, int trackId, int frameCount, double elapsed);

    /// <summary>记录 Gallery 成员注册成功信息。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="memberName">成员名称</param>
    /// <param name="groupId">分组 ID</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Gallery 注册: 成员 {MemberName}, group={GroupId}")]
    public static partial void GalleryMemberEnrolled(ILogger logger, string memberName, string groupId);

    /// <summary>记录成员注册成功信息（名称、ID、分组）。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="memberName">成员名称</param>
    /// <param name="memberId">成员 ID</param>
    /// <param name="groupId">分组 ID</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "成员 {MemberName} 注册成功，ID={MemberId}，Group={GroupId}")]
    public static partial void MemberEnrolled(ILogger logger, string memberName, string memberId, string groupId);

    /// <summary>记录 Gallery 加载失败信息（文件与错误）。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="file">失败的文件路径</param>
    /// <param name="error">错误消息</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Gallery 加载失败: {File}, {Error}")]
    public static partial void GalleryLoadFailed(ILogger logger, string file, string error);

    /// <summary>记录识别结果（成员名、分数、Track ID）。</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="memberName">识别出的成员名称（stranger 时为 "stranger"）</param>
    /// <param name="score">四维融合相似度分数</param>
    /// <param name="trackId">Track ID</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "识别结果: {MemberName}, score={Score:F3}, track={TrackId}")]
    public static partial void RecognitionResult(ILogger logger, string memberName, float score, int trackId);
}
