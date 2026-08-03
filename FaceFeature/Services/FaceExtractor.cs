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
using System.Runtime.InteropServices;

namespace FaceFeature.Services;

/// <summary>
/// ArcFace w600k_r50 ONNX 人脸特征提取器 — 来自 InsightFace buffalo_l 模型包
/// 输入 RGB 人脸裁剪图，输出 512 维 L2 归一化特征向量
/// </summary>
public sealed class FaceExtractor : IDisposable
{
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
    /// <exception cref="FileNotFoundException">models/w600k_r50.onnx 未找到时抛出</exception>
    public FaceExtractor(ILogger<FaceExtractor> logger, IOptions<OnnxSessionOptions> onnxOptions)
    {
        _logger = logger;

        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "w600k_r50.onnx");
        if (!File.Exists(modelPath))
            modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "w600k_r50.onnx");
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("请先运行 scripts/setup_models.py 导出人脸特征模型", modelPath);

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
        if (face.Keypoints is { Length: 5 })
        {
            var sw = Stopwatch.StartNew();
            var aligned = WarpByLandmarks(sourceImage, face.Keypoints);
            Log.FaceAligned(_logger, 5, sw.Elapsed.TotalMilliseconds);
            float sharpness = FaceQuality.EstimateSharpness(aligned);
            Log.FaceSharpness(_logger, sharpness);
            return new FaceExtraction(aligned, sharpness);
        }

        var fallback = AlignFallback(sourceImage, face.Bbox);
        float fallbackSharpness = FaceQuality.EstimateSharpness(fallback);
        Log.FaceSharpness(_logger, fallbackSharpness);
        return new FaceExtraction(fallback, fallbackSharpness);
    }

    /// <summary>
    /// 从已对齐的 112×112 人脸提取 512 维 L2 归一化特征向量
    /// </summary>
    /// <param name="alignedCrop">已对齐的 112×112 人脸图</param>
    /// <returns>L2 归一化的 512 维特征向量（原始字节）</returns>
    public byte[] ExtractFeatures(Image<Rgb24> alignedCrop)
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
            using var results = _session.Run(
                [NamedOnnxValue.CreateFromTensor(inputName, inputTensor)]);

            // 输出解析 — 512 维特征向量
            var output = (DenseTensor<float>)results[0].AsTensor<float>();

            Log.FaceFeatureExtracted(_logger, output.Length, sw.Elapsed.TotalMilliseconds);
            return MemoryMarshal.Cast<float, byte>(output.Buffer.Span).ToArray();
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 五点相似变换仿射到 112×112（逆映射双线性采样，越界填黑）
    /// </summary>
    private static Image<Rgb24> WarpByLandmarks(Image<Rgb24> source, PointF[] landmarks)
    {
        var forward = EstimateSimilarityTransform(landmarks, ArcFaceTemplate);
        Matrix3x2.Invert(forward, out var inverse);

        var aligned = new Image<Rgb24>(InputSize, InputSize);
        aligned.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < InputSize; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < InputSize; x++)
                {
                    float sx = inverse.M11 * x + inverse.M21 * y + inverse.M31;
                    float sy = inverse.M12 * x + inverse.M22 * y + inverse.M32;
                    row[x] = SampleBilinear(source, sx, sy);
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
                    pivot = r;
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
                return Matrix3x2.Identity;

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
    private static Rgb24 SampleBilinear(Image<Rgb24> source, float sx, float sy)
    {
        int w = source.Width, h = source.Height;
        if (sx < 0 || sy < 0 || sx >= w || sy >= h)
            return default;

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
    /// 释放 ONNX Runtime 推理会话
    /// </summary>
    public void Dispose() => _session?.Dispose();
}
