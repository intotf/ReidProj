using FaceFeature.Helpers;
using FaceFeature.Payloads;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Numerics.Tensors;

namespace FaceFeature.Services;

/// <summary>
/// ArcFace ONNX 人脸特征提取器 — 使用 models/glintr100.onnx（如需更换模型请修改代码常量）
/// 输入 RGB 人脸裁剪图，输出 512 维 L2 归一化特征向量
/// </summary>
public sealed class FaceExtractor : IDisposable
{
    /// <summary>ArcFace 特征模型文件名（models 目录下），更换模型时修改此常量</summary>
    private const string ModelFileName = "glintr100.onnx";

    private readonly ILogger<FaceExtractor> _logger;
    private readonly InferenceSession _session;

    /// <summary>ArcFace 期望的输入图像尺寸（112×112）</summary>
    private const int InputSize = 112;
    /// <summary>InsightFace 预处理的像素均值</summary>
    private const float Mean = 127.5f;
    /// <summary>InsightFace 预处理的像素标准差</summary>
    private const float Std = 128f;

    /// <summary>ArcFace 112×112 五点对齐模板（左眼、右眼、鼻尖、左嘴角、右嘴角），与 InsightFace 官方一致</summary>
    private static readonly PointF[] ArcFaceTemplate =
    {
        new(38.2946f, 51.6963f),
        new(73.5318f, 51.5014f),
        new(56.0252f, 71.7366f),
        new(41.5493f, 92.3655f),
        new(70.7299f, 92.2041f),
    };

    /// <summary>
    /// 初始化 ArcFace 人脸特征提取器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="onnxOptions">ONNX Runtime 会话配置</param>
    /// <exception cref="FileNotFoundException">models 下配置的特征模型未找到时抛出</exception>
    public FaceExtractor(ILogger<FaceExtractor> logger, IOptions<OnnxSessionOptions> onnxOptions)
    {
        _logger = logger;

        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", ModelFileName);
        if (!File.Exists(modelPath))
        {
            modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", ModelFileName);
        }
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"请先运行 scripts/setup_models.py 导出人脸特征模型 models/{ModelFileName}", modelPath);
        }

        _session = new InferenceSession(modelPath, onnxOptions.Value.FaceRec);
    }

    /// <summary>
    /// 对齐人脸并评估清晰度 — 优先使用 5 关键点相似变换仿射到 112×112（语义等价 cv2.warpAffine + INTER_LINEAR），
    /// 无关键点时回退为 bbox 外扩裁剪。
    /// </summary>
    /// <param name="sourceImage">原始 RGB 图像</param>
    /// <param name="face">人脸检测结果（边界框 + 关键点）</param>
    /// <returns>对齐后的 112×112 人脸与清晰度分数（Aligned 由调用方负责释放）</returns>
    public FaceExtraction AlignAndScore(Image<Rgb24> sourceImage, FaceBox face)
    {
        Image<Rgb24> aligned;
        if (face.Keypoints is { Length: 5 })
        {
            var sw = Stopwatch.StartNew();
            aligned = WarpByLandmarks(sourceImage, face.Keypoints);
            Log.FaceAligned(_logger, 5, sw.Elapsed.TotalMilliseconds);
        }
        else
        {
            aligned = AlignFallback(sourceImage, face.Bbox);
        }

        try
        {
            float sharpness = EstimateSharpness(aligned);
            Log.FaceSharpness(_logger, sharpness);
            return new FaceExtraction(aligned, sharpness);
        }
        catch
        {
            // 评分失败时释放对齐图，避免 ImageSharp 原生内存泄漏
            aligned.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 从已对齐的 112×112 人脸提取 512 维 L2 归一化特征向量
    /// </summary>
    /// <param name="alignedCrop">已对齐的 112×112 人脸图</param>
    /// <returns>L2 归一化的 512 维特征向量</returns>
    public float[] ExtractFeatures(Image<Rgb24> alignedCrop)
    {
        var sw = Stopwatch.StartNew();

        // 构建 CHW 张量，InsightFace 归一化: (pixel - 127.5) / 128.0
        int planeSize = InputSize * InputSize;
        int tensorSize = 3 * planeSize;
        float[] buffer = ArrayPool<float>.Shared.Rent(tensorSize);
        try
        {
            alignedCrop.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < InputSize; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < InputSize; x++)
                    {
                        var p = row[x];
                        int idx = y * InputSize + x;
                        buffer[idx] = (p.R - Mean) / Std;
                        buffer[planeSize + idx] = (p.G - Mean) / Std;
                        buffer[2 * planeSize + idx] = (p.B - Mean) / Std;
                    }
                }
            });

            var inputTensor = new DenseTensor<float>(buffer.AsMemory(0, tensorSize),
                [1, 3, InputSize, InputSize]);

            // ONNX 推理（自动发现输入/输出名称）
            var inputName = _session.InputMetadata.Keys.First();
            var input = NamedOnnxValue.CreateFromTensor(inputName, inputTensor);
            using var results = _session.Run([input]);

            // 输出解析 — 512 维特征向量
            var output = (DenseTensor<float>)results[0].AsTensor<float>();

            Log.FaceFeatureExtracted(_logger, output.Length, sw.Elapsed.TotalMilliseconds);
            return output.Buffer.Span.ToArray();
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 五点相似变换仿射到 112×112（逆映射双线性采样，越界填黑）
    /// </summary>
    private static Image<Rgb24> WarpByLandmarks(Image<Rgb24> source, ReadOnlySpan<PointF> landmarks)
    {
        var forward = EstimateSimilarityTransform(landmarks, ArcFaceTemplate);
        Matrix3x2.Invert(forward, out var inverse);

        var aligned = new Image<Rgb24>(InputSize, InputSize);

        // 优先使用连续像素内存做随机采样：保留原始逐像素逆映射数学（sx/sy 均依赖 x），
        // 同时避免逐像素 source[x, y] 索引器的边界检查与调用开销；非连续内存时回退索引器。
        if (source.DangerousTryGetSinglePixelMemory(out var sourceMemory)
            && aligned.DangerousTryGetSinglePixelMemory(out var alignedMemory))
        {
            var src = sourceMemory.Span;
            var dst = alignedMemory.Span;
            int srcW = source.Width, srcH = source.Height;

            for (int y = 0; y < InputSize; y++)
            {
                float rowBaseX = inverse.M21 * y + inverse.M31;
                float rowBaseY = inverse.M22 * y + inverse.M32;
                int dstOff = y * InputSize;

                for (int x = 0; x < InputSize; x++)
                {
                    float sx = inverse.M11 * x + rowBaseX;
                    float sy = inverse.M12 * x + rowBaseY;
                    int di = dstOff + x;
                    if (sx < 0 || sy < 0 || sx >= srcW || sy >= srcH)
                    {
                        dst[di] = default;
                        continue;
                    }

                    int sx0 = (int)sx;
                    int sy0 = (int)sy;
                    int sx1 = Math.Min(sx0 + 1, srcW - 1);
                    int sy1 = Math.Min(sy0 + 1, srcH - 1);
                    float fx = sx - sx0;
                    float fy = sy - sy0;

                    int row0 = sy0 * srcW;
                    int row1 = sy1 * srcW;
                    dst[di] = SampleBilinear(
                        src[row0 + sx0], src[row0 + sx1],
                        src[row1 + sx0], src[row1 + sx1], fx, fy);
                }
            }
            return aligned;
        }

        // 兜底：源图内存非连续时使用逐像素索引器（语义与旧实现一致）
        aligned.ProcessPixelRows(dstAcc =>
        {
            for (int y = 0; y < InputSize; y++)
            {
                var dstRow = dstAcc.GetRowSpan(y);
                for (int x = 0; x < InputSize; x++)
                {
                    float sx = inverse.M11 * x + inverse.M21 * y + inverse.M31;
                    float sy = inverse.M12 * x + inverse.M22 * y + inverse.M32;
                    dstRow[x] = SampleBilinear(source, sx, sy);
                }
            }
        });
        return aligned;
    }

    /// <summary>
    /// 最小二乘相似变换估计（解 a,b,tx,ty 四元线性方程组），语义等价 cv2.estimateAffinePartial2D
    /// </summary>
    private static Matrix3x2 EstimateSimilarityTransform(ReadOnlySpan<PointF> src, ReadOnlySpan<PointF> dst)
    {
        // 每个点贡献两行: [px, -py, 1, 0] * [a,b,tx,ty]^T = qx ; [py, px, 0, 1] = qy，法方程 4×4
        Span<double> normal = stackalloc double[16];
        Span<double> rhs = stackalloc double[4];
        Span<double> r1 = stackalloc double[4];
        Span<double> r2 = stackalloc double[4];

        for (int i = 0; i < src.Length; i++)
        {
            double px = src[i].X, py = src[i].Y;
            double qx = dst[i].X, qy = dst[i].Y;

            r1[0] = px; r1[1] = -py; r1[2] = 1; r1[3] = 0;
            r2[0] = py; r2[1] = px; r2[2] = 0; r2[3] = 1;

            for (int m = 0; m < 4; m++)
            {
                rhs[m] += r1[m] * qx + r2[m] * qy;
                for (int n = 0; n < 4; n++)
                {
                    normal[m * 4 + n] += r1[m] * r1[n] + r2[m] * r2[n];
                }
            }
        }

        // 高斯消元（部分主元）求解 4×4 增广矩阵
        Span<double> aug = stackalloc double[20];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                aug[i * 5 + j] = normal[i * 4 + j];
            }
            aug[i * 5 + 4] = rhs[i];
        }

        for (int col = 0; col < 4; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < 4; r++)
            {
                if (Math.Abs(aug[r * 5 + col]) > Math.Abs(aug[pivot * 5 + col]))
                {
                    pivot = r;
                }
            }

            if (pivot != col)
            {
                for (int c = 0; c < 5; c++)
                {
                    (aug[col * 5 + c], aug[pivot * 5 + c]) = (aug[pivot * 5 + c], aug[col * 5 + c]);
                }
            }

            double piv = aug[col * 5 + col];
            if (Math.Abs(piv) < 1e-9)
            {
                return Matrix3x2.Identity;
            }

            for (int r = col + 1; r < 4; r++)
            {
                double f = aug[r * 5 + col] / piv;
                for (int c = col; c < 5; c++)
                {
                    aug[r * 5 + c] -= f * aug[col * 5 + c];
                }
            }
        }

        Span<double> x = stackalloc double[4];
        for (int r = 3; r >= 0; r--)
        {
            double s = aug[r * 5 + 4];
            for (int c = r + 1; c < 4; c++)
            {
                s -= aug[r * 5 + c] * x[c];
            }
            x[r] = s / aug[r * 5 + r];
        }

        float a = (float)x[0], b = (float)x[1];
        return new Matrix3x2(a, b, -b, a, (float)x[2], (float)x[3]);
    }

    /// <summary>
    /// 双线性采样，越界返回黑色（等价 cv2.warpAffine INTER_LINEAR + borderValue=0）
    /// </summary>
    private static Rgb24 SampleBilinear(Rgb24 p00, Rgb24 p10, Rgb24 p01, Rgb24 p11, float fx, float fy)
    {
        float topR = p00.R + (p10.R - p00.R) * fx;
        float topG = p00.G + (p10.G - p00.G) * fx;
        float topB = p00.B + (p10.B - p00.B) * fx;
        float botR = p01.R + (p11.R - p01.R) * fx;
        float botG = p01.G + (p11.G - p01.G) * fx;
        float botB = p01.B + (p11.B - p01.B) * fx;

        return new Rgb24(
            (byte)Math.Clamp(MathF.Round(topR + (botR - topR) * fy), 0, 255),
            (byte)Math.Clamp(MathF.Round(topG + (botG - topG) * fy), 0, 255),
            (byte)Math.Clamp(MathF.Round(topB + (botB - topB) * fy), 0, 255));
    }

    /// <summary>
    /// 双线性采样（逐像素索引器版本，仅用于源图内存非连续时的兜底路径）
    /// </summary>
    private static Rgb24 SampleBilinear(Image<Rgb24> source, float sx, float sy)
    {
        int w = source.Width, h = source.Height;
        if (sx < 0 || sy < 0 || sx >= w || sy >= h)
        {
            return default;
        }

        int x0 = (int)MathF.Floor(sx);
        int y0 = (int)MathF.Floor(sy);
        int x1 = Math.Min(x0 + 1, w - 1);
        int y1 = Math.Min(y0 + 1, h - 1);
        float fx = sx - x0;
        float fy = sy - y0;

        var p00 = source[x0, y0];
        var p10 = source[x1, y0];
        var p01 = source[x0, y1];
        var p11 = source[x1, y1];

        float topR = p00.R + (p10.R - p00.R) * fx;
        float topG = p00.G + (p10.G - p00.G) * fx;
        float topB = p00.B + (p10.B - p00.B) * fx;
        float botR = p01.R + (p11.R - p01.R) * fx;
        float botG = p01.G + (p11.G - p01.G) * fx;
        float botB = p01.B + (p11.B - p01.B) * fx;

        return new Rgb24(
            (byte)Math.Clamp(MathF.Round(topR + (botR - topR) * fy), 0, 255),
            (byte)Math.Clamp(MathF.Round(topG + (botG - topG) * fy), 0, 255),
            (byte)Math.Clamp(MathF.Round(topB + (botB - topB) * fy), 0, 255));
    }

    /// <summary>
    /// 无关键点时的兜底对齐：bbox 外扩 20% → 等比缩放 → 居中 pad
    /// </summary>
    private static Image<Rgb24> AlignFallback(Image<Rgb24> sourceImage, Rectangle faceRect)
    {
        var expanded = ExpandRect(faceRect, 0.2f, sourceImage.Width, sourceImage.Height);
        return sourceImage.Clone(ctx =>
        {
            ctx.Crop(expanded);
            float scale = (float)InputSize / Math.Max(expanded.Width, expanded.Height);
            int newW = (int)(expanded.Width * scale);
            int newH = (int)(expanded.Height * scale);
            ctx.Resize(newW, newH, KnownResamplers.Lanczos3);
            ctx.Pad(InputSize, InputSize, Color.Black);
        });
    }

    /// <summary>
    /// 沿中心扩展人脸边界框，增加上下文信息，再 clamp 到图像边界
    /// </summary>
    private static Rectangle ExpandRect(Rectangle rect, float margin, int maxW, int maxH)
    {
        int expandW = (int)(rect.Width * margin);
        int expandH = (int)(rect.Height * margin);
        int x = Math.Clamp(rect.X - expandW / 2, 0, maxW - 1);
        int y = Math.Clamp(rect.Y - expandH / 2, 0, maxH - 1);
        int w = Math.Clamp(rect.Width + expandW, 1, maxW - x);
        int h = Math.Clamp(rect.Height + expandH, 1, maxH - y);
        return new Rectangle(x, y, w, h);
    }

    /// <summary>
    /// 计算图像清晰度分数：灰度化（Rec.601）→ 3×3 Laplacian → 响应方差（TensorPrimitives 向量化求和）
    /// </summary>
    /// <param name="image">人脸裁剪图（建议对齐后的 112×112）</param>
    /// <returns>Laplacian 方差，数值越大越清晰；图像过小时返回 0</returns>
    private static float EstimateSharpness(Image<Rgb24> image)
    {
        int w = image.Width, h = image.Height;
        if (w < 3 || h < 3)
        {
            return 0f;
        }

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

    /// <summary>
    /// 释放 ONNX Runtime 推理会话
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
    }
}
