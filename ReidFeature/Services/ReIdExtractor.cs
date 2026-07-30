using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ReidFeature.Helpers;
using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReidFeature.Services;

/// <summary>
/// 裁剪类型
/// </summary>
public enum CropType
{
    /// <summary>全身裁剪（默认，bbox 全范围）</summary>
    FullBody,

    /// <summary>头肩区域裁剪（取 bbox 上半 38%，换衣鲁棒）</summary>
    HeadShoulder,
}

/// <summary>
/// FastReID ONNX 特征提取器 — 接收原图+人物框，输出归一化特征向量
/// 支持全身和头肩两种裁剪模式
/// </summary>
public sealed class ReIdExtractor : IDisposable
{
    private readonly ILogger<ReIdExtractor> _logger;
    private readonly InferenceSession _session;

    private const int InputHeight = 256;
    private const int InputWidth = 128;

    /// <summary>头肩裁剪比例 — 取 bbox 上半部分的百分比</summary>
    private const float HeadShoulderRatio = 0.38f;

    /// <summary>
    /// 初始化 ReID 特征提取器，加载 ONNX 模型
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="onnxOptions">ONNX Runtime 配置</param>
    public ReIdExtractor(ILogger<ReIdExtractor> logger, IOptions<OnnxSessionOptions> onnxOptions)
    {
        _logger = logger;

        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "reid_model.onnx");
        if (!File.Exists(modelPath))
        {
            modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "reid_model.onnx");
        }

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("请先运行 scripts/setup_models.py 导出 ReID 模型", modelPath);
        }

        _session = new InferenceSession(modelPath, onnxOptions.Value.ReId);
    }

    /// <summary>
    /// 提取人物特征向量
    /// </summary>
    /// <param name="sourceImage">原始 RGB 图像</param>
    /// <param name="personRect">人物边界框（原图坐标）</param>
    /// <param name="cropType">裁剪类型：FullBody（全身）或 HeadShoulder（头肩）</param>
    /// <returns>L2 归一化的特征向量</returns>
    public byte[] ExtractFeatures(Image<Rgb24> sourceImage, BoundingBox personRect, CropType cropType = CropType.FullBody)
    {
        var sw = Stopwatch.StartNew();

        // 1. Clamp 边界框到图像范围内
        int x = Math.Clamp(personRect.X, 0, sourceImage.Width - 1);
        int y = Math.Clamp(personRect.Y, 0, sourceImage.Height - 1);
        int w = Math.Max(1, Math.Min(personRect.Width, sourceImage.Width - x));
        int h = Math.Max(1, Math.Min(personRect.Height, sourceImage.Height - y));

        // 头肩模式：仅取 bbox 上半部分
        if (cropType == CropType.HeadShoulder)
        {
            h = Math.Max(1, (int)(h * HeadShoulderRatio));
        }

        var rect = new Rectangle(x, y, w, h);

        // 2. 裁剪 → 保持宽高比缩放（长边适配目标尺寸）→ 居中黑色填充至 128×256
        using var processed = sourceImage.Clone(ctx =>
        {
            ctx.Crop(rect);
            float scale = Math.Min((float)InputWidth / w, (float)InputHeight / h);
            int newW = Math.Max(1, (int)(w * scale));
            int newH = Math.Max(1, (int)(h * scale));
            ctx.Resize(newW, newH, KnownResamplers.Lanczos3);
            ctx.Pad(InputWidth, InputHeight, Color.Black);
        });

        // 3. 构建 CHW tensor（原始像素值 [0,1]，mean/std 由 ONNX 图内嵌处理）
        int bufferSize = 3 * InputHeight * InputWidth;
        float[] pixelBuffer = ArrayPool<float>.Shared.Rent(bufferSize);
        try
        {
            processed.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < InputHeight; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < InputWidth; x++)
                    {
                        var p = row[x];
                        int idx = y * InputWidth + x;
                        pixelBuffer[idx] = p.R / 255f;
                        pixelBuffer[InputHeight * InputWidth + idx] = p.G / 255f;
                        pixelBuffer[2 * InputHeight * InputWidth + idx] = p.B / 255f;
                    }
                }
            });
            var inputTensor = new DenseTensor<float>(pixelBuffer.AsMemory(0, bufferSize), [1, 3, InputHeight, InputWidth]);

            // 4. ONNX 推理
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor("input", inputTensor)]);

            // 5. 输出解析 — 特征向量（通过 DenseTensor.Buffer 直接零拷贝输出）
            var resultTensor = (DenseTensor<float>)results[0].AsTensor<float>();

            Log.ReIdFeatureExtracted(_logger, resultTensor.Length, sw.Elapsed.TotalMilliseconds);
            return MemoryMarshal.Cast<float, byte>(resultTensor.Buffer.Span).ToArray();
        }
        finally
        {
            ArrayPool<float>.Shared.Return(pixelBuffer);
        }
    }

    /// <summary>
    /// 释放 ONNX Runtime 会话
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
    }
}
