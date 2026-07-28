using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ReidFeature.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReidFeature.Services;

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
    /// <param name="faceImage">裁剪后的人脸 RGB 图像</param>
    /// <returns>L2 归一化的 512 维特征向量（原始字节）</returns>
    public byte[] ExtractFeatures(Image<Rgb24> faceImage)
    {
        var sw = Stopwatch.StartNew();

        // 1. Resize 到 112×112（ArcFace 期望输入尺寸）
        using var resized = faceImage.Clone(ctx =>
            ctx.Resize(InputSize, InputSize, KnownResamplers.Bicubic));

        // 2. 构建 CHW 张量，InsightFace 归一化: (pixel - 127.5) / 128.0
        int planeSize = InputSize * InputSize;
        int tensorSize = 3 * planeSize;
        float[] buffer = ArrayPool<float>.Shared.Rent(tensorSize);
        try
        {
            resized.ProcessPixelRows(accessor =>
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
    /// 释放 ONNX Runtime 推理会话
    /// </summary>
    public void Dispose() => _session?.Dispose();
}
