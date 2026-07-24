using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ReidFeature.Services;

/// <summary>
/// YOLOv11n-face ONNX 推理 + NMS 后处理，人脸检测
/// </summary>
public sealed class FaceDetector : IDisposable
{
    private readonly ILogger<FaceDetector> _logger;
    private readonly InferenceSession _session;

    // 模型输入尺寸
    private const int InputSize = 640;

    // NMS 参数
    private const float NmsThreshold = 0.4f;
    private const float ConfidenceThreshold = 0.5f;

    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    public FaceDetector(ILogger<FaceDetector> logger)
    {
        _logger = logger;

        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "yolo11n-face.onnx");
        if (!File.Exists(modelPath))
        {
            modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "yolo11n-face.onnx");
        }
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("请先运行 scripts/setup_models.py 导出人脸检测模型", modelPath);
        }

        _logger.LogInformation("加载人脸检测模型: {Path}", modelPath);
        var opts = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        _session = new InferenceSession(modelPath, opts);
        _logger.LogInformation("人脸检测模型加载完成");
    }

    /// <summary>
    /// 检测图像中的人脸，返回边界框列表（坐标相对于输入图像）
    /// </summary>
    public List<(Rectangle Bbox, float Confidence)> Detect(Image<Rgb24> image)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Letterbox resize
        using var resized = LetterboxResize(image, InputSize);

        // 2. 构建 CHW tensor (3×320×320)
        var pixelData = NormalizeToTensor(resized);
        var inputTensor = new DenseTensor<float>(pixelData, [1, 3, InputSize, InputSize]);

        // 3. ONNX 推理
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor("images", inputTensor)]);

        // 4. 解析输出
        var outputData = results[0].AsTensor<float>().ToArray();
        var dims = results[0].AsTensor<float>().Dimensions;
        int numDetections = dims[2];
        int numClasses = dims[1] - 4;
        int stride = numDetections;

        // 5. 框解码 + 置信度过滤
        var candidates = new List<(float X, float Y, float W, float H, float Score)>(numDetections);

        float scale = Math.Min((float)InputSize / image.Width, (float)InputSize / image.Height);
        float padX = (InputSize - (int)(image.Width * scale)) / 2f;
        float padY = (InputSize - (int)(image.Height * scale)) / 2f;

        for (int i = 0; i < numDetections; i++)
        {
            int offset = i;
            float maxScore = 0;
            int bestClass = -1;

            // 找最高分的类别
            for (int c = 0; c < numClasses; c++)
            {
                float score = outputData[(4 + c) * stride + offset];
                if (score > maxScore)
                {
                    maxScore = score;
                    bestClass = c;
                }
            }

            // 置信度阈值过滤
            if (maxScore < ConfidenceThreshold)
                continue;

            // bbox 四通道: cx, cy, w, h（像素坐标，0-320）
            float cx = outputData[0 * stride + offset];
            float cy = outputData[1 * stride + offset];
            float bw = outputData[2 * stride + offset];
            float bh = outputData[3 * stride + offset];

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

            candidates.Add((x1, y1, boxW, boxH, maxScore));
        }

        // 6. NMS
        var results_list = Nms(candidates);

        _logger.LogInformation("人脸检测: {Cnt} 个, 耗时 {Elapsed:F1}ms", results_list.Count, sw.Elapsed.TotalMilliseconds);
        return results_list;
    }

    private static List<(Rectangle Bbox, float Confidence)> Nms(List<(float X, float Y, float W, float H, float Score)> candidates)
    {
        var selected = new List<(Rectangle Bbox, float Confidence)>();
        if (candidates.Count == 0)
            return selected;

        // 按置信度降序排序
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        var removed = new bool[candidates.Count];

        for (int i = 0; i < candidates.Count; i++)
        {
            if (removed[i])
                continue;

            var (x1, y1, w1, h1, score) = candidates[i];
            if (w1 <= 0 || h1 <= 0)
                continue;

            float left1 = Math.Max(0, x1);
            float top1 = Math.Max(0, y1);
            float right1 = left1 + w1;
            float bottom1 = top1 + h1;
            float area1 = w1 * h1;

            selected.Add((new Rectangle(
                (int)left1, (int)top1, (int)(right1 - left1), (int)(bottom1 - top1)), score));

            for (int j = i + 1; j < candidates.Count; j++)
            {
                if (removed[j])
                    continue;

                var (x2, y2, w2, h2, _) = candidates[j];
                if (w2 <= 0 || h2 <= 0)
                    continue;

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
                    continue;

                float interArea = (interRight - interLeft) * (interBottom - interTop);
                float area2 = w2 * h2;
                float iou = interArea / (area1 + area2 - interArea);

                if (iou > NmsThreshold)
                    removed[j] = true;
            }
        }

        return selected;
    }

    private static Image<Rgb24> LetterboxResize(Image<Rgb24> src, int targetSize)
    {
        float scale = Math.Min((float)targetSize / src.Width, (float)targetSize / src.Height);
        int newW = (int)(src.Width * scale);
        int newH = (int)(src.Height * scale);

        using var resized = src.Clone(ctx => ctx.Resize(newW, newH, KnownResamplers.Bicubic));
        var canvas = new Image<Rgb24>(targetSize, targetSize, new Rgb24(114, 114, 114));
        int offsetX = (targetSize - newW) / 2;
        int offsetY = (targetSize - newH) / 2;

        canvas.Mutate(ctx => ctx.DrawImage(resized, new Point(offsetX, offsetY), 1f));
        return canvas;
    }

    private static float[] NormalizeToTensor(Image<Rgb24> image)
    {
        int h = image.Height, w = image.Width;
        var result = new float[3 * h * w];

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    int idx = y * w + x;
                    result[idx] = (p.R / 255f - Mean[0]) / Std[0];
                    result[h * w + idx] = (p.G / 255f - Mean[1]) / Std[1];
                    result[2 * h * w + idx] = (p.B / 255f - Mean[2]) / Std[2];
                }
            }
        });

        return result;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
