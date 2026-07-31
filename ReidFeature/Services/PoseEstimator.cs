using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ReidFeature.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Diagnostics;

namespace ReidFeature.Services;

/// <summary>
/// MoveNet Lightning 姿态估计器 — ONNX 推理 + 体型标量计算
/// 输入：192×192 RGB 图像
/// 输出：17 个 COCO 关键点 (y, x, confidence) + 体型标量 [头身比, 肩髋比]
/// </summary>
public sealed class PoseEstimator : IDisposable
{
    private readonly ILogger<PoseEstimator> _logger;
    private readonly InferenceSession _session;
    private readonly string _inputName;

    private const int InputSize = 192;
    private const int NumKeypoints = 17;

    // COCO 关键点索引
    private const int Nose = 0;
    private const int LeftShoulder = 5;
    private const int RightShoulder = 6;
    private const int LeftHip = 11;
    private const int RightHip = 12;

    /// <summary>
    /// 初始化姿态估计器并加载 MoveNet Lightning 模型
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="onnxOptions">ONNX 会话选项</param>
    public PoseEstimator(ILogger<PoseEstimator> logger, IOptions<OnnxSessionOptions> onnxOptions)
    {
        _logger = logger;

        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "movenet_lightning.onnx");
        if (!File.Exists(modelPath))
        {
            modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "movenet_lightning.onnx");
        }
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("请先运行 scripts/setup_models.py 下载 MoveNet Lightning 模型", modelPath);
        }

        _session = new InferenceSession(modelPath, onnxOptions.Value.Pose);

        // 从模型元数据动态获取输入名，避免硬编码（不同来源的 MoveNet ONNX 输入名可能不同）
        _inputName = _session.InputMetadata.Keys.First();
    }

    /// <summary>
    /// 对人物裁剪图进行姿态估计
    /// </summary>
    /// <param name="crop">人物裁剪图像（任意尺寸，内部会 resize 到 192×192）</param>
    /// <returns>17 个关键点数组 (y, x, confidence)，坐标已映射回裁剪图像素空间</returns>
    public (float Y, float X, float Confidence)[] EstimatePose(Image<Rgb24> crop)
    {
        var sw = Stopwatch.StartNew();

        // 1. Letterbox resize — 保持宽高比缩放到 192×192，居中黑色填充
        //    避免直接拉伸导致人物横纵比例失真，从而影响体型标量（头身比/肩髋比）的准确性
        float scale = Math.Min((float)InputSize / crop.Width, (float)InputSize / crop.Height);
        int newW = Math.Max(1, (int)(crop.Width * scale));
        int newH = Math.Max(1, (int)(crop.Height * scale));
        float padX = (InputSize - newW) / 2f;
        float padY = (InputSize - newH) / 2f;

        using var resized = crop.Clone(ctx =>
        {
            ctx.Resize(newW, newH, KnownResamplers.Lanczos3);
            ctx.Pad(InputSize, InputSize, Color.Black);
        });

        // 2. 构建 NHWC tensor (1×192×192×3)
        //    注意：PINTO 导出的 MoveNet ONNX 内嵌归一化公式为 data * 1.0，
        //    即期望原始 0-255 像素值输入（官方 demo 亦不除以 255），勿再归一化。
        int bufferSize = InputSize * InputSize * 3;
        float[] pixelData = ArrayPool<float>.Shared.Rent(bufferSize);
        try
        {
            resized.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < InputSize; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < InputSize; x++)
                    {
                        int idx = y * InputSize + x;
                        pixelData[idx * 3] = row[x].R;
                        pixelData[idx * 3 + 1] = row[x].G;
                        pixelData[idx * 3 + 2] = row[x].B;
                    }
                }
            });

            var inputTensor = new DenseTensor<float>(
                pixelData.AsMemory(0, bufferSize),
                [1, InputSize, InputSize, 3]);

            // 3. ONNX 推理
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)]);
        
            // 4. 解析输出
            var outputTensor = (DenseTensor<float>)results[0].AsTensor<float>();
            var outputSpan = outputTensor.Buffer.Span;
            var dims = outputTensor.Dimensions;

            // 自适应解析：支持 [1,1,17,3] 和 [1,17,3] 两种常见输出格式
            int kpOffset, kpStride;
            if (dims.Length == 4 && dims[1] == 1 && dims[2] == NumKeypoints && dims[3] == 3)
            {
                // [1, 1, 17, 3]
                kpOffset = 0;
                kpStride = 3;
            }
            else if (dims.Length == 3 && dims[1] == NumKeypoints && dims[2] == 3)
            {
                // [1, 17, 3]
                kpOffset = 0;
                kpStride = 3;
            }
            else
            {
                // 未知格式，尝试展平后按 17×3 读取
                kpOffset = 0;
                kpStride = 3;
            }

            var keypoints = new (float Y, float X, float Confidence)[NumKeypoints];
            for (int i = 0; i < NumKeypoints; i++)
            {
                int baseIdx = kpOffset + i * kpStride;
                float yNorm = outputSpan[baseIdx];
                float xNorm = outputSpan[baseIdx + 1];
                float conf = outputSpan[baseIdx + 2];

                // MoveNet 输出为 [0,1] 归一化坐标（相对 192×192 画布），
                // 反 letterbox 映射回裁剪图像素空间
                keypoints[i] = (
                    (yNorm * InputSize - padY) / scale,
                    (xNorm * InputSize - padX) / scale,
                    conf
                );
            }

            Log.PoseEstimated(_logger, sw.Elapsed.TotalMilliseconds);
            return keypoints;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(pixelData);
        }
    }

    /// <summary>
    /// 从关键点计算体型标量（换衣不变的生物特征）
    /// </summary>
    /// <param name="keypoints">17 个关键点（ReadOnlySpan）</param>
    /// <returns>float[2] = [头身比, 肩髋比]</returns>
    public float[] CalculateBodySignals(ReadOnlySpan<(float Y, float X, float Confidence)> keypoints)
    {
        // 仅使用置信度 > 0.3 的关键点
        float noseY = keypoints[Nose].Confidence > 0.3f ? keypoints[Nose].Y : float.NaN;
        float lsY = keypoints[LeftShoulder].Confidence > 0.3f ? keypoints[LeftShoulder].Y : float.NaN;
        float rsY = keypoints[RightShoulder].Confidence > 0.3f ? keypoints[RightShoulder].Y : float.NaN;
        float lsX = keypoints[LeftShoulder].Confidence > 0.3f ? keypoints[LeftShoulder].X : float.NaN;
        float rsX = keypoints[RightShoulder].Confidence > 0.3f ? keypoints[RightShoulder].X : float.NaN;
        float lhY = keypoints[LeftHip].Confidence > 0.3f ? keypoints[LeftHip].Y : float.NaN;
        float rhY = keypoints[RightHip].Confidence > 0.3f ? keypoints[RightHip].Y : float.NaN;
        float lhX = keypoints[LeftHip].Confidence > 0.3f ? keypoints[LeftHip].X : float.NaN;
        float rhX = keypoints[RightHip].Confidence > 0.3f ? keypoints[RightHip].X : float.NaN;

        // 头身比 = 鼻子→髋中点垂直距离 / 肩→髋中点垂直距离
        float headBodyRatio = 0f;
        if (!float.IsNaN(noseY) && !float.IsNaN(lhY) && !float.IsNaN(rhY) && !float.IsNaN(lsY) && !float.IsNaN(rsY))
        {
            float hipMidY = (lhY + rhY) / 2f;
            float shoulderMidY = (lsY + rsY) / 2f;
            float noseToHip = Math.Abs(noseY - hipMidY);
            float shoulderToHip = Math.Abs(shoulderMidY - hipMidY);
            if (shoulderToHip > 1f)
                headBodyRatio = noseToHip / shoulderToHip;
        }

        // 肩髋比 = 肩宽 / 髋宽
        float shoulderHipRatio = 0f;
        if (!float.IsNaN(lsX) && !float.IsNaN(rsX) && !float.IsNaN(lhX) && !float.IsNaN(rhX))
        {
            float shoulderWidth = Math.Abs(lsX - rsX);
            float hipWidth = Math.Abs(lhX - rhX);
            if (hipWidth > 1f)
                shoulderHipRatio = shoulderWidth / hipWidth;
        }

        return [headBodyRatio, shoulderHipRatio];
    }

    /// <summary>
    /// 释放 ONNX 推理会话
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
    }
}
