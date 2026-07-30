using ReidFeature.Helpers;
using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ReidFeature.Services;

/// <summary>
/// 检测编排服务：YOLO 人物检测 → ByteTrack 跟踪 → TrackFusion 四维特征融合
/// </summary>
public sealed class DetectService
{
    private readonly YoloDetector _yolo;
    private readonly ByteTrackTracker _tracker;
    private readonly TrackFusionService _fusion;
    private readonly ILogger<DetectService> _logger;

    /// <summary>
    /// 当前视频流的帧缓存：trackId → List<(原图, bbox, 置信度)>
    /// </summary>
    private readonly Dictionary<int, List<(Image<Rgb24> Frame, Rectangle Bbox, float Score)>> _trackFrames = [];

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
    /// <param name="frameIndex">帧序号</param>
    /// <returns>跟踪结果列表 (trackId, bbox, 置信度)</returns>
    public List<(int TrackId, Rectangle Bbox, float Score)> ProcessVideoFrame(Image<Rgb24> image, int frameIndex)
    {
        var detections = _yolo.DetectPersons(image);
        if (detections.Count == 0)
            return [];

        var input = detections.Select(d => (d.Bbox, d.Confidence)).ToList();
        var tracked = _tracker.Update(input, frameIndex);

        var results = new List<(int, Rectangle, float)>();
        for (int i = 0; i < tracked.Count; i++)
        {
            var (trackId, bbox) = tracked[i];
            float score = detections.FirstOrDefault(d => d.Bbox == bbox).Confidence;

            // 缓存到 Track 队列
            if (!_trackFrames.ContainsKey(trackId))
                _trackFrames[trackId] = [];
            _trackFrames[trackId].Add((image.Clone(ctx => { }), bbox, score));

            results.Add((trackId, bbox, score));
        }

        return results;
    }

    /// <summary>
    /// 获取所有已完成 Track 的四维特征融合结果
    /// 需在视频流处理完毕后调用
    /// </summary>
    /// <param name="minFrames">Track 最小存活帧数门槛</param>
    /// <returns>人物检测结果列表（包含 TrackFeaturePack）</returns>
    public List<PersonDetection> FlushCompletedTracks(int minFrames = 10)
    {
        var completed = _tracker.FlushCompletedTracks(minFrames);
        var results = new List<PersonDetection>();

        for (int i = 0; i < completed.Count; i++)
        {
            var (trackId, startFrame, endFrame, firstBbox, lastBbox, centers) = completed[i];

            if (!_trackFrames.TryGetValue(trackId, out var frames))
                continue;

            // 该 Track 的帧已经通过 ProcessVideoFrame 缓存
            var pack = _fusion.FuseTrack(trackId, frames);

            // 释放缓存的帧
            foreach (var (frame, _, _) in frames)
                frame.Dispose();

            results.Add(new PersonDetection(
                FrameIndex: startFrame,
                Bbox: new BoundingBox(firstBbox.X, firstBbox.Y, firstBbox.Width, firstBbox.Height),
                Confidence: 1.0f,
                Features: pack.VecCloth,
                FeaturePack: pack));
        }

        _trackFrames.Clear();
        return results;
    }

    /// <summary>
    /// 直接处理单张图像中的所有人（非视频流模式）
    /// 注意：请勿与 ProcessVideoFrame 混用
    /// </summary>
    public List<PersonDetection> DetectPersons(Image<Rgb24> image, DetectionFlags flags)
    {
        var detections = _yolo.DetectPersons(image);
        var results = new List<PersonDetection>();

        for (int i = 0; i < detections.Count; i++)
        {
            var (box, conf) = detections[i];

            var boundingBox = new BoundingBox(box.X, box.Y, box.Width, box.Height);

            results.Add(new PersonDetection(
                FrameIndex: 0,
                Bbox: boundingBox,
                Confidence: conf,
                Features: [],
                FeaturePack: null));
        }

        return results;
    }

    /// <summary>
    /// 重置跟踪器和帧缓存（新视频流开始前调用）
    /// </summary>
    public void Reset()
    {
        _tracker.Reset();
        _trackFrames.Clear();
    }
}
