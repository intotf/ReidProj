using ReidFeature.Helpers;
using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Runtime.InteropServices;

namespace ReidFeature.Services;

/// <summary>
/// 检测编排服务：YOLO 人物检测 → ByteTrack 跟踪 → TrackFusion 四维特征融合
/// </summary>
public sealed class DetectService : IDisposable
{
    private readonly YoloDetector _yolo;
    private readonly ByteTrackTracker _tracker;
    private readonly TrackFusionService _fusion;
    private readonly ILogger<DetectService> _logger;

    /// <summary>
    /// 当前视频流的帧缓存：trackId → List&lt;(原图, bbox, 置信度)&gt;
    /// </summary>
    private readonly Dictionary<int, List<(Image<Rgb24> Frame, Rectangle Bbox, float Score)>> _trackFrames = [];

    /// <summary>当前视频流的抽帧间隔（秒），用于步态频率换算；≤0 表示全部帧模式</summary>
    private double _frameIntervalSeconds;

    /// <summary>
    /// 初始化检测编排服务
    /// </summary>
    /// <param name="yolo">YOLO 人物检测器</param>
    /// <param name="tracker">ByteTrack 跟踪器</param>
    /// <param name="fusion">四维特征融合服务</param>
    /// <param name="logger">日志记录器</param>
    public DetectService(
        YoloDetector yolo,
        ByteTrackTracker tracker,
        TrackFusionService fusion,
        ILogger<DetectService> logger)
    {
        _yolo = yolo;
        _tracker = tracker;
        _fusion = fusion;
        _logger = logger;
    }

    /// <summary>
    /// 处理一帧视频图像：YOLO 检测 → ByteTrack 跟踪
    /// 将当前帧的检测结果按 TrackId 缓存
    /// </summary>
    /// <param name="image">当前帧 RGB 图像</param>
    private void ProcessVideoFrame(Image<Rgb24> image)
    {
        var detections = _yolo.DetectPersons(image);
        // 无论是否有检测都让 tracker 推进：无人帧触发丢失逻辑（LostFrames++ / HitStreak--）
        var tracked = _tracker.Update(CollectionsMarshal.AsSpan(detections));
        if (detections.Count == 0)
        {
            return;
        }

        // 每帧构建一次 bbox→置信度 映射，避免循环内 O(n²) 线性查找
        var scoreByBbox = new Dictionary<Rectangle, float>(detections.Count);
        for (int i = 0; i < detections.Count; i++)
        {
            scoreByBbox[detections[i].Bbox] = detections[i].Confidence;
        }

        for (int i = 0; i < tracked.Count; i++)
        {
            var (trackId, bbox) = tracked[i];
            float score = scoreByBbox.TryGetValue(bbox, out var s) ? s : 0f;

            // 缓存到 Track 队列
            if (!_trackFrames.TryGetValue(trackId, out var frames))
            {
                frames = [];
                _trackFrames[trackId] = frames;
            }
            // 缓存 bbox 裁剪图而非整帧，大幅降低内存占用
            var cropBbox = BoundingBoxHelper.ClampToBounds(bbox, image.Width, image.Height);
            // bbox 同步转为裁剪图局部坐标（左上角为原点），保持与 Frame 图像同坐标系
            frames.Add((
                image.Clone(ctx => ctx.Crop(cropBbox)),
                new Rectangle(0, 0, cropBbox.Width, cropBbox.Height),
                score));
        }
    }

    /// <summary>
    /// 统一的视频流处理循环：解码 → 逐帧检测/跟踪/缓存
    /// </summary>
    /// <param name="request">HTTP 请求（读取请求体视频流）</param>
    /// <param name="codec">视频编码格式</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理成功返回 true；解码失败返回 false</returns>
    public async Task<bool> ProcessVideoStreamAsync(
        HttpRequest request,
        VideoCodec codec,
        ILogger logger,
        double frameIntervalSeconds,
        CancellationToken cancellationToken)
    {
        _frameIntervalSeconds = frameIntervalSeconds;

        var enumerable = VideoDecoder.DecodeFramesAsync(
            request.Body, codec, logger, frameIntervalSeconds, cancellationToken);
        await using var enumerator = enumerable.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            Image<Rgb24> image;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    return true;
                }
                image = enumerator.Current;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    DisposeAllTrackFrames();
                    throw;
                }
                Log.VideoDecodeFailed(logger, ex);
                DisposeAllTrackFrames();
                return false;
            }

            using (image)
            {
                ProcessVideoFrame(image);
            }
        }
    }

    /// <summary>
    /// 获取所有已完成 Track 的四维特征融合结果
    /// 需在视频流处理完毕后调用
    /// </summary>
    /// <returns>人物检测结果列表（包含 TrackFeaturePack）</returns>
    public List<PersonDetection> FlushCompletedTracks()
    {
        var completed = _tracker.FlushCompletedTracks();
        var results = new List<PersonDetection>();

        for (int i = 0; i < completed.Count; i++)
        {
            var (trackId, firstBbox, lastBbox, centers) = completed[i];

            if (!_trackFrames.TryGetValue(trackId, out var frames))
            {
                continue;
            }

            try
            {
                // 该 Track 的帧已经通过 ProcessVideoFrame 缓存
                var pack = _fusion.FuseTrack(trackId, CollectionsMarshal.AsSpan(frames), centers, _frameIntervalSeconds);

                results.Add(new PersonDetection(
                    Bbox: new BoundingBox(firstBbox.X, firstBbox.Y, firstBbox.Width, firstBbox.Height),
                    Confidence: 1.0f,
                    Features: pack.VecCloth,
                    FeaturePack: pack,
                    TrackId: trackId));
            }
            catch (Exception ex)
            {
                Log.DetectPipelineFailed(_logger, ex);
            }
            finally
            {
                // 无论融合成功与否都释放该 Track 的缓存帧，避免 Image 泄漏
                foreach (var (frame, _, _) in frames)
                {
                    frame.Dispose();
                }
            }
        }

        _trackFrames.Clear();
        return results;
    } 

    /// <summary>
    /// 释放全部 Track 的缓存裁剪图（解码失败 / 请求取消时调用，避免 Image 非托管内存泄漏）
    /// </summary>
    private void DisposeAllTrackFrames()
    {
        foreach (var frames in _trackFrames.Values)
        {
            foreach (var (frame, _, _) in frames)
            {
                frame.Dispose();
            }
        }
        _trackFrames.Clear();
    }

    /// <summary>
    /// 请求结束时的最终兜底：释放尚未清空的缓存裁剪图
    /// 正常路径已在 FlushCompletedTracks / 解码失败路径中释放，此处仅防御异常传播等未覆盖路径
    /// 注：注入的 YoloDetector / TrackFusionService 为 Singleton，生命周期由 DI 容器管理，不在此释放
    /// </summary>
    public void Dispose()
    {
        DisposeAllTrackFrames();
    }
}
