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
/// YOLOv11n ONNX 推理 + NMS 后处理，仅过滤人物（class=0）
/// </summary>
public sealed class YoloDetector : IDisposable
{
    private readonly ILogger<YoloDetector> _logger;
    private readonly InferenceSession _session;

    // 模型输入尺寸
    private const int InputSize = 640;

    // NMS 参数
    private const float NmsThreshold = 0.45f;
    private const float ConfidenceThreshold = 0.20f;

    // YOLO 预处理：像素值归一化到 [0,1]（不做 ImageNet 标准化，与 Ultralytics 默认一致）
    private static readonly float[] Mean = [0f, 0f, 0f];
    private static readonly float[] Std = [1f, 1f, 1f];

    // YOLO letterbox 填充色：灰色(114)（Ultralytics 训练/推理标准）
    private static readonly Color LetterboxFillColor =
        Color.FromPixel(new Rgba32(114, 114, 114, byte.MaxValue));

    /// <summary>
    /// 初始化 YOLO 人物检测器，加载 ONNX 模型
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="onnxOptions">ONNX Runtime 配置</param>
    public YoloDetector(ILogger<YoloDetector> logger, IOptions<OnnxSessionOptions> onnxOptions)
    {
        _logger = logger;

        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "yolo11n.onnx");
        if (!File.Exists(modelPath))
        {
            // 回退到项目目录
            modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "yolo11n.onnx");
        }
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("请先运行 scripts/setup_models.py 导出 YOLO 模型", modelPath);
        }

        _session = new InferenceSession(modelPath, onnxOptions.Value.Yolo);
    }

    /// <summary>
    /// 检测图像中的人物，返回边界框列表
    /// </summary>
    /// <param name="image">输入 RGB 图像</param>
    /// <returns>人物边界框列表（每个元素包含矩形框和置信度），无人时返回空列表</returns>
    public List<(Rectangle Bbox, float Confidence)> DetectPersons(Image<Rgb24> image)
    {
        var sw = Stopwatch.StartNew();

        // 1. Letterbox resize
        using var resized = LetterboxResize(image, InputSize);

        // 2. 构建 CHW tensor (3×640×640)
        int bufferSize = 3 * InputSize * InputSize;
        float[] pixelData = ArrayPool<float>.Shared.Rent(bufferSize);
        try
        {
            NormalizeToTensor(resized, pixelData);
            var inputTensor = new DenseTensor<float>(pixelData.AsMemory(0, bufferSize), [1, 3, InputSize, InputSize]);

            // 3. ONNX 推理
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor("images", inputTensor)]);

            // 4. 解析输出
            // YOLOv11 输出 shape: [1, 84, 8400]
            // 84 = 4(cx,cy,w,h) + 80(COCO class scores)，只读取人物(class=0)的分数
            // Detect head 已内嵌 sigmoid(cls) + decode_bboxes(xywh) * strides
            // bbox 四通道为像素坐标 (0-640)，cls 已过 sigmoid
            var outputTensor = (DenseTensor<float>)results[0].AsTensor<float>();
            var outputSpan = outputTensor.Buffer.Span;
            var dims = outputTensor.Dimensions;
            int numDetections = dims[2];
            int stride = numDetections; // 每个通道的步长

            // 5. 框解码 + 置信度过滤（使用临时列表）
            var candidates = new List<(float X, float Y, float W, float H, float Score)>(numDetections);

            // letterbox 参数必须与 LetterboxResize 保持一致
            float scale = Math.Min((float)InputSize / image.Width, (float)InputSize / image.Height);
            float padX = (InputSize - (int)(image.Width * scale)) / 2f;
            float padY = (InputSize - (int)(image.Height * scale)) / 2f;

            for (int i = 0; i < numDetections; i++)
            {
                // 只检查人物类别（class 0）的置信度
                float score = outputSpan[4 * stride + i];
                if (score < ConfidenceThreshold)
                {
                    continue;
                }

                // bbox 四通道: cx, cy, w, h（像素坐标，0-640）
                float cx = outputSpan[0 * stride + i];
                float cy = outputSpan[1 * stride + i];
                float bw = outputSpan[2 * stride + i];
                float bh = outputSpan[3 * stride + i];

                // cx,cy,w,h → x1,y1,x2,y2（仍在 letterbox 空间）
                float x1_lb = cx - bw / 2f;
                float y1_lb = cy - bh / 2f;
                float x2_lb = cx + bw / 2f;
                float y2_lb = cy + bh / 2f;

                // 反 letterbox 映射回原图坐标
                float x1 = (x1_lb - padX) / scale;
                float y1 = (y1_lb - padY) / scale;
                float x2 = (x2_lb - padX) / scale;
                float y2 = (y2_lb - padY) / scale;

                // Clamp 到原图范围内
                x1 = Math.Clamp(x1, 0f, image.Width);
                y1 = Math.Clamp(y1, 0f, image.Height);
                x2 = Math.Clamp(x2, 0f, image.Width);
                y2 = Math.Clamp(y2, 0f, image.Height);

                float boxW = Math.Max(0, x2 - x1);
                float boxH = Math.Max(0, y2 - y1);

                candidates.Add((
                    x1,
                    y1,
                    boxW,
                    boxH,
                    score
                ));
            }

            // 6. NMS
            var resultsList = Nms(CollectionsMarshal.AsSpan(candidates));

            Log.YoloDetectionCompleted(_logger, resultsList.Count, sw.Elapsed.TotalMilliseconds);
            return resultsList;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(pixelData);
        }
    }

    /// <summary>
    /// Letterbox resize — 保持宽高比缩放到 targetSize，多余部分用灰色(114)填充
    /// </summary>
    private static Image<Rgb24> LetterboxResize(Image<Rgb24> src, int targetSize)
    {
        float scale = Math.Min((float)targetSize / src.Width, (float)targetSize / src.Height);
        int newW = Math.Max(1, (int)(src.Width * scale));
        int newH = Math.Max(1, (int)(src.Height * scale));

        // YOLO 官方 letterbox 标准：灰色(114)居中填充（训练/推理一致）
        return src.Clone(ctx =>
        {
            ctx.Resize(newW, newH, KnownResamplers.Lanczos3);
            ctx.Pad(targetSize, targetSize, LetterboxFillColor);
        });
    }


    /// <summary>
    /// 将 Image 类型图像归一化为模型输入的 Tensor 格式
    /// </summary>
    /// <param name="image">输入图像（已进行 Letterbox Resize）</param>
    /// <param name="tensorData">预分配的浮点数 Span，大小应为 3 × 640 × 640</param>
    private static void NormalizeToTensor(Image<Rgb24> image, Span<float> tensorData)
    {
        int h = image.Height, w = image.Width;
        var frame = image.Frames[0];
        var buffer = frame.PixelBuffer;
        for (int y = 0; y < h; y++)
        {
            var row = buffer.DangerousGetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                var p = row[x];
                int idx = y * w + x;
                tensorData[idx] = (p.R / 255f - Mean[0]) / Std[0];
                tensorData[h * w + idx] = (p.G / 255f - Mean[1]) / Std[1];
                tensorData[2 * h * w + idx] = (p.B / 255f - Mean[2]) / Std[2];
            }
        }
    }

    private static List<(Rectangle Bbox, float Confidence)> Nms(Span<(float X, float Y, float W, float H, float Score)> candidates)
    {
        var selected = new List<(Rectangle Bbox, float Confidence)>();
        if (candidates.IsEmpty)
        {
            return selected;
        }

        // 按置信度降序排序
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        int count = candidates.Length;
        // count 约 8400（~8.4KB），从 ArrayPool 租借避免每次分配
        bool[] removedBuffer = ArrayPool<bool>.Shared.Rent(count);
        try
        {
            Array.Clear(removedBuffer, 0, count);
            var removed = removedBuffer.AsSpan(0, count);

            for (int i = 0; i < count; i++)
            {
                if (removed[i])
                {
                    continue;
                }

                var (x1, y1, w1, h1, score) = candidates[i];
                // 跳过无效框（宽度或高度非正数）
                if (w1 <= 0 || h1 <= 0)
                {
                    continue;
                }

                float left1 = Math.Max(0, x1);
                float top1 = Math.Max(0, y1);
                float right1 = left1 + w1;
                float bottom1 = top1 + h1;
                float area1 = w1 * h1;

                selected.Add((new Rectangle(
                    (int)left1, (int)top1, (int)(right1 - left1), (int)(bottom1 - top1)), score));

                for (int j = i + 1; j < count; j++)
                {
                    if (removed[j])
                    {
                        continue;
                    }

                    var (x2, y2, w2, h2, _) = candidates[j];
                    if (w2 <= 0 || h2 <= 0)
                    {
                        continue;
                    }

                    float left2 = Math.Max(0, x2);
                    float top2 = Math.Max(0, y2);
                    float right2 = left2 + w2;
                    float bottom2 = top2 + h2;

                    // 计算 IoU
                    float interLeft = Math.Max(left1, left2);
                    float interTop = Math.Max(top1, top2);
                    float interRight = Math.Min(right1, right2);
                    float interBottom = Math.Min(bottom1, bottom2);

                    if (interLeft >= interRight || interTop >= interBottom)
                    {
                        continue;
                    }

                    float interArea = (interRight - interLeft) * (interBottom - interTop);
                    float area2 = w2 * h2;
                    float iou = interArea / (area1 + area2 - interArea);

                    if (iou > NmsThreshold)
                    {
                        removed[j] = true;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<bool>.Shared.Return(removedBuffer);
        }

        return selected;
    }

    /// <summary>
    /// 释放 ONNX Runtime 会话
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
    }
}
