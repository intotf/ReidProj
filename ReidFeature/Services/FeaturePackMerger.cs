using System.Buffers;
using System.Numerics.Tensors;
using ReidFeature.Payloads;

namespace ReidFeature.Services;

/// <summary>
/// 多段特征包合并工具 —— 将同一人的多段视频特征（默认等权）融合为一个特征包。
/// 全身/头肩 ReID 向量：加权平均后 L2 归一化；体型/步态标量：逐元素加权平均。
/// 用于"同一人多段注册"与"成员合并去重"。
/// </summary>
public static class FeaturePackMerger
{
    /// <summary>
    /// 按权重（默认等权）融合多个特征包
    /// </summary>
    /// <param name="packs">同一人的多个特征包（至少 1 个）</param>
    /// <param name="weights">可选权重数组，长度须与 packs 一致；缺省时等权</param>
    /// <returns>融合后的特征包；packs 为空时返回空特征包</returns>
    public static TrackFeaturePack WeightedAverage(
        IReadOnlyList<TrackFeaturePack> packs,
        IReadOnlyList<float>? weights = null)
    {
        if (packs.Count == 0)
        {
            return new TrackFeaturePack();
        }
        if (packs.Count == 1)
        {
            return Clone(packs[0]);
        }

        int count = packs.Count;
        float[] weightBuffer = ArrayPool<float>.Shared.Rent(count);
        try
        {
            Span<float> ws = weightBuffer.AsSpan(0, count);
            if (weights is { Count: > 0 } && weights.Count == count)
            {
                for (int i = 0; i < count; i++)
                {
                    ws[i] = weights[i];
                }
            }
            else
            {
                ws.Fill(1f);
            }

            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                total += ws[i];
            }
            if (total <= 0f)
            {
                ws.Fill(1f / count);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    ws[i] /= total;
                }
            }

            return new TrackFeaturePack
            {
                VecCloth = WeightedAverageVector(packs, static p => p.VecCloth, ws),
                VecHead = WeightedAverageVector(packs, static p => p.VecHead, ws),
                BodySignals = WeightedAverageScalar(packs, static p => p.BodySignals, ws),
                GaitSignals = WeightedAverageScalar(packs, static p => p.GaitSignals, ws),
            };
        }
        finally
        {
            ArrayPool<float>.Shared.Return(weightBuffer);
        }
    }

    /// <summary>
    /// 向量维度加权平均 + L2 归一化（维度不一致的样本跳过）
    /// </summary>
    private static float[] WeightedAverageVector(
        IReadOnlyList<TrackFeaturePack> packs,
        Func<TrackFeaturePack, float[]> selector,
        ReadOnlySpan<float> weights)
    {
        int dim = 0;
        for (int i = 0; i < packs.Count; i++)
        {
            var vec = selector(packs[i]);
            if (vec.Length > 0)
            {
                dim = vec.Length;
                break;
            }
        }
        if (dim == 0)
        {
            return [];
        }

        float[] poolBuffer = ArrayPool<float>.Shared.Rent(dim);
        try
        {
            Span<float> avg = poolBuffer.AsSpan(0, dim);
            avg.Clear();

            float usedWeight = 0f;
            for (int i = 0; i < packs.Count; i++)
            {
                var vec = selector(packs[i]);
                if (vec.Length != dim)
                {
                    continue;
                }
                TensorPrimitives.MultiplyAdd(vec, weights[i], avg, avg);
                usedWeight += weights[i];
            }
            if (usedWeight <= 0f)
            {
                return new float[dim];
            }

            float norm = TensorPrimitives.Norm(avg);
            if (norm > 1e-8f)
            {
                TensorPrimitives.Divide(avg, norm, avg);
            }
            return avg.ToArray();
        }
        finally
        {
            ArrayPool<float>.Shared.Return(poolBuffer);
        }
    }

    /// <summary>
    /// 标量维度逐元素加权平均（维度不一致的样本跳过）
    /// </summary>
    private static float[] WeightedAverageScalar(
        IReadOnlyList<TrackFeaturePack> packs,
        Func<TrackFeaturePack, float[]> selector,
        ReadOnlySpan<float> weights)
    {
        int dim = 0;
        for (int i = 0; i < packs.Count; i++)
        {
            var vec = selector(packs[i]);
            if (vec.Length > 0)
            {
                dim = vec.Length;
                break;
            }
        }
        if (dim == 0)
        {
            return [0f, 0f];
        }

        var result = new float[dim];
        float usedWeight = 0f;
        for (int i = 0; i < packs.Count; i++)
        {
            var vec = selector(packs[i]);
            if (vec.Length != dim)
            {
                continue;
            }
            for (int d = 0; d < dim; d++)
            {
                result[d] += vec[d] * weights[i];
            }
            usedWeight += weights[i];
        }
        if (usedWeight > 0f)
        {
            for (int d = 0; d < dim; d++)
            {
                result[d] /= usedWeight;
            }
        }
        return result;
    }

    /// <summary>
    /// 复制特征包（避免多段合并时共享数组引用）
    /// </summary>
    private static TrackFeaturePack Clone(TrackFeaturePack pack) => new()
    {
        VecCloth = [.. pack.VecCloth],
        VecHead = [.. pack.VecHead],
        BodySignals = [.. pack.BodySignals],
        GaitSignals = [.. pack.GaitSignals],
    };
}
