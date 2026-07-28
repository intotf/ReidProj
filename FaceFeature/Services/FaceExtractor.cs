using FaceFeature.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Diagnostics;
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
    /// 提取人脸特征向量
    /// </summary>
    /// <param name="sourceImage">原始 RGB 图像</param>
    /// <param name="faceRect">人脸边界框（原图坐标）</param>
    /// <returns>L2 归一化的 512 维特征向量（原始字节）</returns>
    public byte[] ExtractFeatures(Image<Rgb24> sourceImage, Rectangle faceRect)
    {
        var sw = Stopwatch.StartNew();

        // 0. 扩展人脸框 20% 以获得更多头部轮廓上下文，再 clamp 到图像边界
        var expanded = ExpandRect(faceRect, 0.2f, sourceImage.Width, sourceImage.Height);

        // 1. 裁剪 → 保持宽高比缩放（长边=112）→ 居中 pad 到 112×112
        //    避免直接拉伸破坏人脸比例，黑色填充不影响归一化后的特征主体
        using var processed = sourceImage.Clone(ctx =>
        {
            ctx.Crop(expanded);
            float scale = (float)InputSize / Math.Max(expanded.Width, expanded.Height);
            int newW = (int)(expanded.Width * scale);
            int newH = (int)(expanded.Height * scale);
            ctx.Resize(newW, newH, KnownResamplers.Lanczos3);
            ctx.Pad(InputSize, InputSize, Color.Black);
        });

        // 2. 构建 CHW 张量，InsightFace 归一化: (pixel - 127.5) / 128.0
        int planeSize = InputSize * InputSize;
        int tensorSize = 3 * planeSize;
        float[] buffer = ArrayPool<float>.Shared.Rent(tensorSize);
        try
        {
            processed.ProcessPixelRows(accessor =>
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

            // 3. ONNX 推理（自动发现输入/输出名称）
            var inputName = _session.InputMetadata.Keys.First();
            using var results = _session.Run(
                [NamedOnnxValue.CreateFromTensor(inputName, inputTensor)]);

            // 4. 输出解析 — 512 维特征向量
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
