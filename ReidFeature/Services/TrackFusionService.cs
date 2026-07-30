using ReidFeature.Helpers;
using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;

namespace ReidFeature.Services;

/// <summary>
/// Track 内四维特征融合服务
/// 对一个 Track 的所有帧执行：全身 ReID + 头肩 ReID + 姿态体型 + 轨迹步态
/// 按框大小×置信度进行质量加权融合
/// </summary>
public sealed class TrackFusionService
{
    private readonly ReIdExtractor _reid;
    private readonly PoseEstimator _pose;
    private readonly ByteTrackTracker _tracker;
    private readonly ILogger<TrackFusionService> _logger;

    public TrackFusionService(
        ReIdExtractor reid,
        PoseEstimator pose,
        ByteTrackTracker tracker,
        ILogger<TrackFusionService> logger)
    {
        _reid = reid;
        _pose = pose;
        _tracker = tracker;
        _logger = logger;
    }

    /// <summary>
    /// 对单个 Track 内所有帧进行四维特征融合
    /// </summary>
    /// <param name="trackId">ByteTrack Track ID</param>
    /// <param name="frames">该 Track 内所有帧的 (原图, bbox, 检测置信度)</param>
    /// <returns>质量加权融合后的四维特征包</returns>
    public TrackFeaturePack FuseTrack(
        int trackId,
        List<(Image<Rgb24> Frame, Rectangle Bbox, float Score)> frames)
    {
        var sw = Stopwatch.StartNew();

        if (frames.Count == 0)
            return new TrackFeaturePack();

        // 计算每帧的质量权重 = bbox 面积 × 检测置信度
        float maxArea = frames.Max(f => (float)f.Bbox.Width * f.Bbox.Height);
        var weights = new float[frames.Count];
        float totalWeight = 0f;
        for (int i = 0; i < frames.Count; i++)
        {
            float area = frames[i].Bbox.Width * frames[i].Bbox.Height;
            weights[i] = (area / maxArea) * frames[i].Score;
            totalWeight += weights[i];
        }
        if (totalWeight <= 0f) totalWeight = 1f;

        // 取 K 个最高权重帧做特征提取（避免全帧计算，降低延迟）
        int topK = Math.Min(5, frames.Count);
        var topIndices = Enumerable.Range(0, frames.Count)
            .OrderByDescending(i => weights[i])
            .Take(topK)
            .ToList();

        // 全身 ReID 特征（cloth）
        byte[][] clothFeatures = new byte[topK][];
        // 头肩 ReID 特征（head）
        byte[][] headFeatures = new byte[topK][];
        // 体型标量
        float[][] bodySignals = new float[topK][];

        // 对 Top-K 帧并行提取
        Parallel.For(0, topK, i =>
        {
            int idx = topIndices[i];
            var (frame, bbox, _) = frames[idx];
            var boundingBox = new BoundingBox(bbox.X, bbox.Y, bbox.Width, bbox.Height);

            // 全身 ReID
            clothFeatures[i] = _reid.ExtractFeatures(frame, boundingBox, CropType.FullBody);

            // 头肩 ReID
            headFeatures[i] = _reid.ExtractFeatures(frame, boundingBox, CropType.HeadShoulder);

            // 姿态 → 体型标量
            using var crop = frame.Clone(ctx => ctx.Crop(bbox));
            var keypoints = _pose.EstimatePose(crop);
            bodySignals[i] = _pose.CalculateBodySignals(keypoints);
        });

        // 按权重融合特征向量（加权逐元素平均 + L2 归一化）
        byte[] fusedCloth = WeightedAverageFeatures(clothFeatures, topIndices.Select(i => weights[i]).ToArray(), totalWeight);
        byte[] fusedHead = WeightedAverageFeatures(headFeatures, topIndices.Select(i => weights[i]).ToArray(), totalWeight);

        // 体型标量：加权平均
        float[] fusedBody = new float[2];
        for (int i = 0; i < topK; i++)
        {
            float w = weights[topIndices[i]] / totalWeight;
            if (bodySignals[i].Length >= 2)
            {
                fusedBody[0] += bodySignals[i][0] * w;
                fusedBody[1] += bodySignals[i][1] * w;
            }
        }

        // 步态标量：从 ByteTrack 轨迹中心点计算
        var centers = _tracker.GetTrackCenters(trackId);
        float[] fusedGait = ComputeGaitSignals(centers);

        Log.TrackFusionCompleted(_logger, trackId, frames.Count, sw.Elapsed.TotalMilliseconds);

        return new TrackFeaturePack
        {
            VecCloth = fusedCloth,
            VecHead = fusedHead,
            BodySignals = fusedBody,
            GaitSignals = fusedGait,
        };
    }

    /// <summary>
    /// 加权融合特征向量 — 逐元素加权平均后 L2 归一化
    /// </summary>
    private static byte[] WeightedAverageFeatures(byte[][] features, float[] weights, float totalWeight)
    {
        if (features.Length == 0 || features[0].Length == 0)
            return [];

        int dim = features[0].Length;
        var avg = new float[dim / 4];

        for (int i = 0; i < features.Length; i++)
        {
            if (features[i] == null || features[i].Length != dim) continue;
            float w = weights[i] / totalWeight;
            var vec = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(features[i]);
            for (int j = 0; j < avg.Length; j++)
                avg[j] += vec[j] * w;
        }

        // L2 归一化
        float norm = MathF.Sqrt(avg.Sum(v => v * v));
        if (norm > 1e-8f)
        {
            for (int j = 0; j < avg.Length; j++)
                avg[j] /= norm;
        }

        return System.Runtime.InteropServices.MemoryMarshal.Cast<float, byte>(avg).ToArray();
    }

    /// <summary>
    /// 从 ByteTrack 轨迹计算步态标量 [步频(Hz), 水平摆幅(px)]
    /// 步频 = 中心点垂直振荡的零交叉频率
    /// 摆幅 = 水平位置的标准差
    /// </summary>
    private static float[] ComputeGaitSignals(PointF[] centers)
    {
        if (centers.Length < 10)
            return [0f, 0f];

        // 步频：Y 方向零交叉法
        int zeroCrossings = 0;
        for (int i = 1; i < centers.Length; i++)
        {
            float dy = centers[i].Y - centers[i - 1].Y;
            if (i > 1)
            {
                float prevDy = centers[i - 1].Y - centers[i - 2].Y;
                if (prevDy > 0 && dy <= 0)
                    zeroCrossings++;
            }
        }
        float stepFrequency = zeroCrossings / 2f;

        // 水平摆幅：X 位置的标准差
        float meanX = centers.Average(c => c.X);
        float varianceX = centers.Sum(c => (c.X - meanX) * (c.X - meanX)) / centers.Length;
        float swingAmplitude = MathF.Sqrt(varianceX);

        return [stepFrequency, swingAmplitude];
    }
}
