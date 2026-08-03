using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;
using System.Numerics.Tensors;

namespace FaceFeature.Services;

/// <summary>
/// 人脸清晰度评估 — 基于人脸 crop 的 Laplacian 方差（越大越清晰），纯代码实现、无额外依赖
/// </summary>
public static class FaceQuality
{
    /// <summary>
    /// 计算图像清晰度分数：灰度化（Rec.601）→ 3×3 Laplacian → 响应方差（TensorPrimitives 向量化求和）
    /// </summary>
    /// <param name="image">人脸裁剪图（建议对齐后的 112×112）</param>
    /// <returns>Laplacian 方差，数值越大越清晰；图像过小时返回 0</returns>
    public static float EstimateSharpness(Image<Rgb24> image)
    {
        int w = image.Width, h = image.Height;
        if (w < 3 || h < 3)
            return 0f;

        int planeSize = w * h;
        int responseCount = (w - 2) * (h - 2);

        // ArrayPool：租用数组的实际长度可能大于申请长度，必须按实际长度切片使用
        byte[] lumaRented = ArrayPool<byte>.Shared.Rent(planeSize);
        float[] responsesRented = ArrayPool<float>.Shared.Rent(responseCount);
        try
        {
            image.ProcessPixelRows(acc =>
            {
                for (int y = 0; y < h; y++)
                {
                    var row = acc.GetRowSpan(y);
                    int off = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        var p = row[x];
                        lumaRented[off + x] = (byte)((p.R * 299 + p.G * 587 + p.B * 114) / 1000);
                    }
                }
            });

            var luma = lumaRented.AsSpan(0, planeSize);
            var responses = responsesRented.AsSpan(0, responseCount);
            int idx = 0;
            for (int y = 1; y < h - 1; y++)
            {
                int off = y * w;
                for (int x = 1; x < w - 1; x++)
                {
                    int p = off + x;
                    responses[idx++] = -4 * luma[p] + luma[p - w] + luma[p + w] + luma[p - 1] + luma[p + 1];
                }
            }

            float sum = TensorPrimitives.Sum(responses);
            float sumSq = TensorPrimitives.SumOfSquares(responses);
            float mean = sum / responseCount;
            return sumSq / responseCount - mean * mean;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lumaRented);
            ArrayPool<float>.Shared.Return(responsesRented);
        }
    }
}
