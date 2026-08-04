using FaceFeature.Payloads;
using System.Buffers;
using System.Numerics.Tensors;

namespace FaceFeature.Helpers;

/// <summary>
/// 视频多帧融合 — 消费逐帧检测流，按质量加权增量累积特征，经共识门控剔除离群帧，
/// 融合向量连续收敛或达到帧数上限时提前完成。一个视频输入只产出一个融合结果。
/// </summary>
internal static class FaceVideoFusion
{
    /// <summary>
    /// 消费逐帧检测流并产出融合结果；无任何检测帧时返回 null
    /// </summary>
    /// <param name="frames">逐帧检测结果流</param>
    /// <param name="maxFrames">融合帧数上限（&gt;0）；达到上限时即使未收敛也立即完成</param>
    /// <param name="options">融合配置（质量加权、共识门控与收敛早停参数）</param>
    /// <param name="logger">日志记录器（用于离群帧剔除日志）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<FusedFaceResult?> FuseAsync(
        IAsyncEnumerable<FaceDetection> frames,
        int maxFrames,
        FaceFusionOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var enumerator = frames.WithCancellation(cancellationToken).GetAsyncEnumerator();
        if (!await enumerator.MoveNextAsync())
        {
            return null;
        }

        // 对配置参数做防御性钳制，避免非法配置导致过早收敛或门控失效
        int minFrames = Math.Max(1, options.MinFrames);
        int stableRequired = Math.Max(1, options.StableRequired);
        int warmup = Math.Max(1, options.ConsensusWarmup);
        float stabilityCosine = Math.Clamp(options.StabilityCosine, 0f, 1f);
        float consensusGate = Math.Clamp(options.ConsensusGate, 0f, 1f);

        // 以第一帧确定特征维度，初始化累加器与两块轮换缓冲区
        FaceDetection representative = enumerator.Current;
        int length = representative.Features.Length;
        var weightedSum = new float[length];
        var bufferA = ArrayPool<float>.Shared.Rent(length);
        var bufferB = ArrayPool<float>.Shared.Rent(length);
        var temp = ArrayPool<float>.Shared.Rent(length);
        float[] previousFused = bufferB; // 占位，hasPrevious 为 false 时不会读取
        float[] latestFused = bufferB;   // 占位，hasFused 为 false 时不会读取
        int acceptedCount = 1;
        int stableRun = 0;
        bool early = false;
        bool hasPrevious = false;
        bool hasFused = false;
        bool writeToB = false;

        float firstWeight = QualityWeight(representative);
        Accumulate(weightedSum, representative.Features, firstWeight, temp);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                FaceDetection detection = enumerator.Current;

                // 共识门控：预热期无条件接受；之后与当前融合向量余弦低于阈值视为离群帧跳过
                if (acceptedCount >= warmup && hasFused)
                {
                    float consensus = Cosine(detection.Features.AsSpan(0, length), latestFused.AsSpan(0, length));
                    if (consensus < consensusGate)
                    {
                        Log.FaceFusionSkippedOutlier(logger, consensus, consensusGate);
                        continue;
                    }
                }

                float weight = QualityWeight(detection);
                Accumulate(weightedSum, detection.Features, weight, temp);
                acceptedCount++;

                if (detection.Confidence > representative.Confidence)
                {
                    representative = detection;
                }

                // 达到预热帧数后即可产出融合估计（用于门控与稳定性判断）
                if (acceptedCount >= warmup)
                {
                    float[] fused = writeToB ? bufferB : bufferA;
                    Normalize(weightedSum, fused.AsSpan(0, length));
                    latestFused = fused;
                    hasFused = true;

                    // 收敛早停：样本充足后，相邻融合向量连续稳定才判定收敛
                    if (acceptedCount >= minFrames)
                    {
                        if (hasPrevious)
                        {
                            float cosine = Cosine(previousFused.AsSpan(0, length), fused.AsSpan(0, length));
                            stableRun = cosine >= stabilityCosine ? stableRun + 1 : 0;
                        }

                        if (stableRun >= stableRequired || acceptedCount >= maxFrames)
                        {
                            early = true;
                            break;
                        }
                    }
                    previousFused = fused;
                    hasPrevious = true;
                    writeToB = !writeToB;
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(bufferA);
            ArrayPool<float>.Shared.Return(bufferB);
            ArrayPool<float>.Shared.Return(temp);
        }

        // 结果数组需随 FusedFaceResult 存活，不能直接使用池缓冲区
        var resultFeatures = new float[length];
        Normalize(weightedSum, resultFeatures);

        return new FusedFaceResult(
            resultFeatures,
            representative.Bbox,
            representative.Confidence,
            representative.Sharpness,
            acceptedCount,
            early);
    }

    /// <summary>
    /// 单帧融合权重：confidence × sharpness，清晰度与置信度越高权重越大；
    /// 无清晰度分数时回退为置信度
    /// </summary>
    private static float QualityWeight(FaceDetection detection)
    {
        float weight = detection.Confidence * detection.Sharpness;
        return weight > 0f ? weight : detection.Confidence;
    }

    /// <summary>按权重将单帧特征向量累加到加权累加器（weightedSum += weight × features）</summary>
    private static void Accumulate(
        Span<float> weightedSum,
        ReadOnlySpan<float> features,
        float weight,
        Span<float> temp)
    {
        TensorPrimitives.Multiply(features, weight, temp);
        TensorPrimitives.Add(weightedSum, temp, weightedSum);
    }

    /// <summary>
    /// 加权均值 + L2 归一化得到融合特征（均值方向与加权累加和一致，无需除以总权重）；
    /// 结果写入调用方提供的缓冲区，不分配数组。
    /// </summary>
    private static void Normalize(ReadOnlySpan<float> weightedSum, Span<float> destination)
    {
        weightedSum.CopyTo(destination);

        float norm = TensorPrimitives.Norm<float>(destination);
        if (norm > 0)
        {
            TensorPrimitives.Divide(destination, norm, destination);
        }
    }

    /// <summary>两个 L2 归一化特征的余弦相似度（等价点积）</summary>
    private static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        return TensorPrimitives.Dot(a, b);
    }
}
