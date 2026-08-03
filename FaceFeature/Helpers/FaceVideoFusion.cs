using FaceFeature.Payloads;
using System.Numerics.Tensors;

namespace FaceFeature.Helpers;

/// <summary>
/// 视频多帧融合 — 消费逐帧检测流，增量累积特征，融合向量连续收敛或达到帧数上限时提前完成。
/// 一个视频输入只产出一个融合结果。
/// </summary>
internal static class FaceVideoFusion
{
    /// <summary>融合至少需要积累的帧数，避免在样本不足时提前收敛</summary>
    private const int MinFrames = 3;

    /// <summary>相邻融合向量余弦达到该值时视为一次“稳定”</summary>
    private const float StabilityCosine = 0.99f;

    /// <summary>连续稳定次数达到该值时提前完成融合</summary>
    private const int StableRequired = 2;

    /// <summary>
    /// 消费逐帧检测流并产出融合结果；无任何检测帧时返回 null
    /// </summary>
    /// <param name="frames">逐帧检测结果流</param>
    /// <param name="maxFrames">融合帧数上限（&gt;0）；达到上限时即使未收敛也立即完成</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<FusedFaceResult?> FuseAsync(
        IAsyncEnumerable<FaceDetection> frames,
        int maxFrames,
        CancellationToken cancellationToken)
    {
        float[]? accumulator = null;
        float[]? previousFused = null;
        int stableRun = 0;
        int count = 0;
        FaceDetection? representative = null;
        float[]? resultFeatures = null;
        bool early = false;

        await foreach (var detection in frames.WithCancellation(cancellationToken))
        {
            accumulator ??= new float[detection.Features.Length];
            Accumulate(accumulator, detection.Features);
            count++;

            if (representative is null || detection.Confidence > representative.Confidence)
            {
                representative = detection;
            }

            if (count >= MinFrames)
            {
                var fused = Normalize(accumulator);
                if (previousFused is not null)
                {
                    float cosine = Cosine(previousFused, fused);
                    stableRun = cosine >= StabilityCosine ? stableRun + 1 : 0;
                    if (stableRun >= StableRequired || count >= maxFrames)
                    {
                        resultFeatures = fused;
                        early = true;
                        break;
                    }
                }
                previousFused = fused;
            }
        }

        if (count == 0 || representative is null)
        {
            return null;
        }

        resultFeatures ??= Normalize(accumulator!);

        return new FusedFaceResult(
            resultFeatures,
            representative.Bbox,
            representative.Confidence,
            representative.Sharpness,
            count,
            early);
    }

    /// <summary>将单帧特征向量累加到融合累加器（逐元素相加）</summary>
    private static void Accumulate(Span<float> accumulator, ReadOnlySpan<float> features)
    {
        TensorPrimitives.Add(accumulator, features, accumulator);
    }

    /// <summary>均值 + L2 归一化得到融合特征（均值方向与累加和一致，无需除以帧数）</summary>
    private static float[] Normalize(ReadOnlySpan<float> accumulator)
    {
        var acc = new float[accumulator.Length];
        accumulator.CopyTo(acc);

        float norm = TensorPrimitives.Norm<float>(acc);
        if (norm > 0)
        {
            TensorPrimitives.Divide(acc, norm, acc);
        }

        return acc;
    }

    /// <summary>两个 L2 归一化特征的余弦相似度（等价点积）</summary>
    private static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => TensorPrimitives.Dot(a, b);
}
