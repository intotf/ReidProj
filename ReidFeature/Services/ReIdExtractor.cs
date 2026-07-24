using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReidFeature.Services;

/// <summary>
/// FastReID ONNX 特征提取器 — 接收 RGB 裁剪图，输出归一化特征向量
/// </summary>
public sealed class ReIdExtractor : IDisposable
{
    private readonly ILogger<ReIdExtractor> _logger;
    private readonly InferenceSession _session;

    private const int InputHeight = 256;
    private const int InputWidth = 128;

    public ReIdExtractor(ILogger<ReIdExtractor> logger)
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

        _logger.LogInformation("加载 ReID 模型: {Path}", modelPath);
        var opts = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        _session = new InferenceSession(modelPath, opts);
        _logger.LogInformation("ReID 模型加载完成");
    }

    /// <summary>
    /// 提取人物特征向量
    /// </summary>
    /// <param name="personImage">裁剪后的人物 RGB 图像</param>
    /// <returns>L2 归一化的特征向量</returns>
    public byte[] ExtractFeatures(Image<Rgb24> personImage)
    {
        var sw = Stopwatch.StartNew();

        // 1. Resize 到 256×128（ReID 期望输入尺寸）
        using var resized = personImage.Clone(ctx =>
            ctx.Resize(InputWidth, InputHeight, KnownResamplers.Bicubic));

        // 2. 构建 CHW tensor（原始像素值 [0,1]，mean/std 由 ONNX 图内嵌处理）
        int h = resized.Height, w = resized.Width;
        int bufferSize = 3 * h * w;
        float[] pixelBuffer = ArrayPool<float>.Shared.Rent(bufferSize);
        try
        {
            resized.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < h; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                    {
                        var p = row[x];
                        int idx = y * w + x;
                        pixelBuffer[idx] = p.R / 255f;
                        pixelBuffer[h * w + idx] = p.G / 255f;
                        pixelBuffer[2 * h * w + idx] = p.B / 255f;
                    }
                }
            });
            var inputTensor = new DenseTensor<float>(pixelBuffer.AsMemory(0, bufferSize), [1, 3, InputHeight, InputWidth]);

            // 3. ONNX 推理
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor("input", inputTensor)]);

            // 4. 输出解析 — 特征向量（通过 DenseTensor.Buffer 直接零拷贝输出）
            var resultTensor = (DenseTensor<float>)results[0].AsTensor<float>();

            _logger.LogInformation("ReID 特征: dim={Dim}, 耗时 {Elapsed:F1}ms", resultTensor.Length, sw.Elapsed.TotalMilliseconds);
            return MemoryMarshal.Cast<float, byte>(resultTensor.Buffer.Span).ToArray();
        }
        finally
        {
            ArrayPool<float>.Shared.Return(pixelBuffer);
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
