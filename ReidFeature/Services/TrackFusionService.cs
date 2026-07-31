using ReidFeature.Helpers;
using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
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
    /// <remarks>frames 中的 bbox 必须与 Frame 图像同坐标系（Frame 为 bbox 裁剪图时，bbox 应为裁剪图局部坐标，左上角为原点）</remarks>
    public TrackFeaturePack FuseTrack(
        int trackId,
        ReadOnlySpan<(Image<Rgb24> Frame, Rectangle Bbox, float Score)> frames,
        ReadOnlySpan<PointF> centers = default)
    {
        var sw = Stopwatch.StartNew();

        int frameCount = frames.Length;
        if (frameCount == 0)
        {
            return new TrackFeaturePack();
        }

        // 计算每帧的质量权重 = bbox 面积 × 检测置信度（线性扫描，无需 LINQ）
        float maxArea = 0f;
        for (int i = 0; i < frameCount; i++)
        {
            float area = frames[i].Bbox.Width * frames[i].Bbox.Height;
            if (area > maxArea)
            {
                maxArea = area;
            }
        }
        // 防御：无有效面积（bbox 宽高为 0）时无法计算权重，直接返回空包
        if (maxArea <= 0f)
        {
            return new TrackFeaturePack();
        }

        float[] weightsBuffer = ArrayPool<float>.Shared.Rent(frameCount);
        try
        {
            // ArrayPool 返回的数组实际长度 ≥ 请求值，用 Span 限定有效范围
            Span<float> weightsSpan = weightsBuffer.AsSpan(0, frameCount);

            float totalWeight = 0f;
            for (int i = 0; i < weightsSpan.Length; i++)
            {
                float area = frames[i].Bbox.Width * frames[i].Bbox.Height;
                weightsSpan[i] = (area / maxArea) * frames[i].Score;
                totalWeight += weightsSpan[i];
            }
            if (totalWeight <= 0f)
            {
                totalWeight = 1f;
            }

            // 取 K 个最高权重帧做特征提取（避免全帧计算，降低延迟）
            // topK ≤ 5：Top-Frames 为小堆数组（供并行闭包捕获），权重栈上分配
            int topK = Math.Min(5, frameCount);
            var topFrames = new (Image<Rgb24> Frame, Rectangle Bbox, float Score)[topK];
            Span<float> topWeights = stackalloc float[topK];
            SelectTopFrames(frames, weightsSpan, topFrames, topWeights);

            // 全身 ReID 特征（cloth）
            byte[][] clothFeatures = new byte[topK][];
            // 头肩 ReID 特征（head）
            byte[][] headFeatures = new byte[topK][];
            // 体型标量
            var bodySignals = new (float HeadBody, float ShoulderHip)[topK];

            // 对 Top-K 帧并行提取
            Parallel.For(0, topK, i =>
            {
                var (frame, bbox, _) = topFrames[i];
                var boundingBox = new BoundingBox(bbox.X, bbox.Y, bbox.Width, bbox.Height);

                // 全身 ReID
                clothFeatures[i] = _reid.ExtractFeatures(frame, boundingBox, CropType.FullBody);

                // 头肩 ReID
                headFeatures[i] = _reid.ExtractFeatures(frame, boundingBox, CropType.HeadShoulder);

                // 姿态 → 体型标量
                var safeBbox = BoundingBoxHelper.ClampToBounds(bbox, frame.Width, frame.Height);
                using var crop = frame.Clone(ctx => ctx.Crop(safeBbox));
                var keypoints = _pose.EstimatePose(crop);
                bodySignals[i] = _pose.CalculateBodySignals(keypoints);
            });

            // 按权重融合特征向量（加权逐元素平均 + L2 归一化）
            byte[] fusedCloth = WeightedAverageFeatures(clothFeatures, topWeights, totalWeight);
            byte[] fusedHead = WeightedAverageFeatures(headFeatures, topWeights, totalWeight);

            // 体型标量：加权平均
            float[] fusedBody = new float[2];
            for (int i = 0; i < topK; i++)
            {
                float w = topWeights[i] / totalWeight;
                fusedBody[0] += bodySignals[i].HeadBody * w;
                fusedBody[1] += bodySignals[i].ShoulderHip * w;
            }

            // 步态标量：从 ByteTrack 轨迹中心点计算
            // 注意：调用方通常在 FlushCompletedTracks（已把 track 从字典移除）之后调用本方法，
            // 此时 _tracker.GetTrackCenters 已取不到轨迹点，因此优先使用调用方传入的 centers。
            if (centers.IsEmpty)
            {
                centers = _tracker.GetTrackCenters(trackId);
            }
            (float stepFrequency, float swingAmplitude) = ComputeGaitSignals(centers);

            Log.TrackFusionCompleted(_logger, trackId, frameCount, sw.Elapsed.TotalMilliseconds);

            return new TrackFeaturePack
            {
                VecCloth = fusedCloth,
                VecHead = fusedHead,
                BodySignals = fusedBody,
                GaitSignals = [stepFrequency, swingAmplitude],
            };
        }
        finally
        {
            ArrayPool<float>.Shared.Return(weightsBuffer);
        }
    }

    /// <summary>
    /// 从权重数组中选取 Top-K 帧（O(n·K) 插入式维护，替代 LINQ 排序分配）
    /// </summary>
    private static void SelectTopFrames(
        ReadOnlySpan<(Image<Rgb24> Frame, Rectangle Bbox, float Score)> frames,
        ReadOnlySpan<float> weights,
        Span<(Image<Rgb24> Frame, Rectangle Bbox, float Score)> topFrames,
        Span<float> topWeights)
    {
        int filled = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            float w = weights[i];
            if (filled < topFrames.Length)
            {
                topFrames[filled] = frames[i];
                topWeights[filled] = w;
                filled++;
                // 冒泡保持按权重降序
                for (int j = filled - 1; j > 0 && topWeights[j] > topWeights[j - 1]; j--)
                {
                    (topFrames[j], topFrames[j - 1]) = (topFrames[j - 1], topFrames[j]);
                    (topWeights[j], topWeights[j - 1]) = (topWeights[j - 1], topWeights[j]);
                }
            }
            else if (w > topWeights[topFrames.Length - 1])
            {
                topFrames[topFrames.Length - 1] = frames[i];
                topWeights[topFrames.Length - 1] = w;
                for (int j = topFrames.Length - 1; j > 0 && topWeights[j] > topWeights[j - 1]; j--)
                {
                    (topFrames[j], topFrames[j - 1]) = (topFrames[j - 1], topFrames[j]);
                    (topWeights[j], topWeights[j - 1]) = (topWeights[j - 1], topWeights[j]);
                }
            }
        }
    }

    /// <summary>
    /// 加权融合特征向量 — 逐元素加权平均后 L2 归一化
    /// 累加缓冲从 ArrayPool 租借，MultiplyAdd 单趟 SIMD 融合
    /// </summary>
    private static byte[] WeightedAverageFeatures(ReadOnlySpan<byte[]> features, ReadOnlySpan<float> weights, float totalWeight)
    {
        if (features.Length == 0 || features[0].Length == 0)
        {
            return [];
        }

        int dim = features[0].Length;
        int floatDim = dim / 4;
        float[] poolBuffer = ArrayPool<float>.Shared.Rent(floatDim);
        try
        {
            Span<float> avg = poolBuffer.AsSpan(0, floatDim);
            // ArrayPool 残留脏数据，必须先清零（MultiplyAdd 是累加语义）
            avg.Clear();

            for (int i = 0; i < features.Length; i++)
            {
                if (features[i] == null || features[i].Length != dim)
                {
                    continue;
                }
                float w = weights[i] / totalWeight;
                var vec = MemoryMarshal.Cast<byte, float>(features[i]);
                // 单趟融合: avg = avg + vec * w（destination 与 addend 重叠）
                TensorPrimitives.MultiplyAdd(vec, w, avg, avg);
            }

            // L2 归一化
            float norm = TensorPrimitives.Norm(avg);
            if (norm > 1e-8f)
            {
                TensorPrimitives.Divide(avg, norm, avg);
            }

            return MemoryMarshal.Cast<float, byte>(avg).ToArray();
        }
        finally
        {
            ArrayPool<float>.Shared.Return(poolBuffer);
        }
    }

    /// <summary>
    /// 从 ByteTrack 轨迹计算步态标量 [步频(Hz), 水平摆幅(px)]
    /// 步频 = 中心点垂直振荡的零交叉频率
    /// 摆幅 = 水平位置的标准差
    /// </summary>
    private static (float StepFrequency, float SwingAmplitude) ComputeGaitSignals(ReadOnlySpan<PointF> centers)
    {
        // 最少 3 个轨迹点：步频零交叉需要连续两个差分，摆幅需要标准差
        if (centers.Length < 3)
        {
            return (0f, 0f);
        }

        // 步频：Y 方向零交叉法
        int zeroCrossings = 0;
        for (int i = 1; i < centers.Length; i++)
        {
            float dy = centers[i].Y - centers[i - 1].Y;
            if (i > 1)
            {
                float prevDy = centers[i - 1].Y - centers[i - 2].Y;
                if (prevDy > 0 && dy <= 0)
                {
                    zeroCrossings++;
                }
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

        return (stepFrequency, swingAmplitude);
    }
}
