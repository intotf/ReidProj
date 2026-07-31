using ReidFeature.Helpers;
using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;

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

    /// <summary>
    /// 初始化 Track 四维特征融合服务
    /// </summary>
    /// <param name="reid">ReID 特征提取器</param>
    /// <param name="pose">姿态估计器</param>
    /// <param name="tracker">ByteTrack 跟踪器</param>
    /// <param name="logger">日志记录器</param>
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
    /// <param name="centers">轨迹中心点序列（可选，用于步态特征）</param>
    /// <returns>质量加权融合后的四维特征包</returns>
    public TrackFeaturePack FuseTrack(
        int trackId,
        List<(Image<Rgb24> Frame, Rectangle Bbox, float Score)> frames,
        PointF[]? centers = null)
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
            var safeBbox = ClampToBounds(bbox, frame.Width, frame.Height);
            using var crop = frame.Clone(ctx => ctx.Crop(safeBbox));
            var keypoints = _pose.EstimatePose(crop);
            bodySignals[i] = _pose.CalculateBodySignals(keypoints);
        });

        // 按权重融合特征向量（加权逐元素平均 + L2 归一化）
        // Top-K ≤ 5，栈上分配权重数组，避免为每个特征重复 ToArray
        Span<float> topWeights = stackalloc float[topK];
        for (int i = 0; i < topK; i++)
            topWeights[i] = weights[topIndices[i]];

        byte[] fusedCloth = WeightedAverageFeatures(clothFeatures, topWeights, totalWeight);
        byte[] fusedHead = WeightedAverageFeatures(headFeatures, topWeights, totalWeight);

        // 体型标量：加权平均
        float[] fusedBody = new float[2];
        for (int i = 0; i < topK; i++)
        {
            float w = topWeights[i] / totalWeight;
            if (bodySignals[i].Length >= 2)
            {
                fusedBody[0] += bodySignals[i][0] * w;
                fusedBody[1] += bodySignals[i][1] * w;
            }
        }

        // 步态标量：从 ByteTrack 轨迹中心点计算
        // 注意：调用方通常在 FlushCompletedTracks（已把 track 从字典移除）之后调用本方法，
        // 此时 _tracker.GetTrackCenters 已取不到轨迹点，因此优先使用调用方传入的 centers。
        centers ??= _tracker.GetTrackCenters(trackId);
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
    private static byte[] WeightedAverageFeatures(ReadOnlySpan<byte[]> features, ReadOnlySpan<float> weights, float totalWeight)
    {
        if (features.Length == 0 || features[0].Length == 0)
            return [];

        int dim = features[0].Length;
        var avg = new float[dim / 4];
        var scaled = new float[avg.Length];

        for (int i = 0; i < features.Length; i++)
        {
            if (features[i] == null || features[i].Length != dim) continue;
            float w = weights[i] / totalWeight;
            var vec = MemoryMarshal.Cast<byte, float>(features[i]);
            TensorPrimitives.Multiply(vec, w, scaled);
            TensorPrimitives.Add(avg, scaled, avg);
        }

        // L2 归一化
        float norm = TensorPrimitives.Norm(avg);
        if (norm > 1e-8f)
            TensorPrimitives.Divide(avg, norm, avg);

        return MemoryMarshal.Cast<float, byte>(avg).ToArray();
    }

    /// <summary>
    /// 将 bbox 裁剪到图像边界内，避免检测框越界导致 Crop 抛异常
    /// </summary>
    private static Rectangle ClampToBounds(Rectangle rect, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            return Rectangle.Empty;

        int x = Math.Clamp(rect.X, 0, imageWidth - 1);
        int y = Math.Clamp(rect.Y, 0, imageHeight - 1);
        int right = Math.Clamp(rect.X + rect.Width, x + 1, imageWidth);
        int bottom = Math.Clamp(rect.Y + rect.Height, y + 1, imageHeight);

        return new Rectangle(x, y, right - x, bottom - y);
    }

    /// <summary>
    /// 从 ByteTrack 轨迹计算步态标量 [步频(Hz), 水平摆幅(px)]
    /// 步频 = 中心点垂直振荡的零交叉频率
    /// 摆幅 = 水平位置的标准差
    /// </summary>
    private static float[] ComputeGaitSignals(ReadOnlySpan<PointF> centers)
    {
        // 最少 3 个轨迹点：步频零交叉需要连续两个差分，摆幅需要标准差
        if (centers.Length < 3)
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
        float meanX = 0f;
        for (int i = 0; i < centers.Length; i++)
            meanX += centers[i].X;
        meanX /= centers.Length;
         

        float varianceX = 0f;
        for (int i = 0; i < centers.Length; i++)
            varianceX += (centers[i].X - meanX) * (centers[i].X - meanX);
        varianceX /= centers.Length;
        float swingAmplitude = MathF.Sqrt(varianceX);

        return [stepFrequency, swingAmplitude];
    }
}
