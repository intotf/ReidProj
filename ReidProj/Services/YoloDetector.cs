using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ReidProj.Services;

/// <summary>
/// YOLOv11n ONNX 推理 + NMS 后处理，仅过滤人物（class=0）
/// </summary>
public sealed class YoloDetector : IDisposable
{
    private readonly ILogger<YoloDetector> _logger;
    private readonly ImageUtils _imageUtils;
    private readonly InferenceSession _session;

    // COCO 类别中 person 的索引
    private const int PersonClassId = 0;

    // 模型输入尺寸
    private const int InputSize = 640;

    // NMS 参数
    private const float NmsThreshold = 0.3f;
    private const float ConfidenceThreshold = 0.35f;

    public YoloDetector(ILogger<YoloDetector> logger, ImageUtils imageUtils)
    {
        _logger = logger;
        _imageUtils = imageUtils;

        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "yolo11n.onnx");
        if (!File.Exists(modelPath))
        {
            // 回退到项目目录
            modelPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "models", "yolo11n.onnx");
        }
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                "请先运行 scripts/setup_models.py 导出 YOLO 模型", modelPath);
        }

        _logger.LogInformation("加载 YOLO 模型: {Path}", modelPath);
        var opts = new Microsoft.ML.OnnxRuntime.SessionOptions();
        opts.GraphOptimizationLevel = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL;
        opts.IntraOpNumThreads = 1;
        opts.InterOpNumThreads = 1;
        opts.ExecutionMode = Microsoft.ML.OnnxRuntime.ExecutionMode.ORT_SEQUENTIAL;
        _session = new InferenceSession(modelPath, opts);
        _logger.LogInformation("YOLO 模型加载完成, 输入: {Cnt}", _session.InputMetadata.Count);
    }

    /// <summary>
    /// 检测图像中的人物，返回边界框列表
    /// </summary>
    public List<(Rectangle Bbox, float Confidence)> Detect(Image<Rgb24> image)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Letterbox resize
        using var resized = _imageUtils.LetterboxResize(image, InputSize);

        // 2. 构建 CHW tensor (3×640×640)
        var pixelData = _imageUtils.NormalizeToTensor(resized);
        var inputTensor = new DenseTensor<float>(pixelData, [1, 3, InputSize, InputSize]);

        // 3. ONNX 推理
        using var results = _session.Run(
            [NamedOnnxValue.CreateFromTensor("images", inputTensor)]);

        // 4. 解析输出
        // YOLOv11 输出 shape: [1, 84, 8400]
        // 84 = 4(cx,cy,w,h) + 80(COCO class scores)
        // Detect head 已内嵌 sigmoid(cls) + decode_bboxes(xywh) * strides
        // bbox 四通道为像素坐标 (0-640)，cls 已过 sigmoid
        var outputData = results[0].AsTensor<float>().ToArray();
        var outputDims = results[0].AsTensor<float>().Dimensions;
        int numDetections = outputDims[2]; // 8400
        int numClasses = outputDims[1] - 4; // 80
        int stride = numDetections; // 每个通道的步长（8400）



        // 5. 框解码 + 置信度过滤（使用临时列表）
        var candidates = new List<(float X, float Y, float W, float H, float Score)>(numDetections);

        // letterbox 参数必须与 ImageUtils.LetterboxResize 保持一致
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
                // ONNX 图中已内嵌 sigmoid，直接读取即可
                float score = outputData[(4 + c) * stride + offset];
                if (score > maxScore)
                {
                    maxScore = score;
                    bestClass = c;
                }
            }

            // 只保留人物 + 置信度阈值过滤
            if (bestClass != PersonClassId || maxScore < ConfidenceThreshold)
                continue;

            // bbox 四通道: cx, cy, w, h（像素坐标，0-640）
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

            candidates.Add((
                x1,
                y1,
                boxW,
                boxH,
                maxScore
            ));
        }


        // 6. NMS
        var results_list = Nms(candidates);

        _logger.LogInformation("YOLO 检测: {Cnt} 人, 耗时 {Elapsed:F1}ms", results_list.Count, sw.Elapsed.TotalMilliseconds);
        return results_list;
    }

    private List<(Rectangle Bbox, float Confidence)> Nms(
        List<(float X, float Y, float W, float H, float Score)> candidates)
    {
        var selected = new List<(Rectangle Bbox, float Confidence)>();
        if (candidates.Count == 0) return selected;

        // 按置信度降序排序
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        var removed = new bool[candidates.Count];

        for (int i = 0; i < candidates.Count; i++)
        {
            if (removed[i]) continue;

            var (x1, y1, w1, h1, score) = candidates[i];
            // 跳过无效框（宽度或高度非正数）
            if (w1 <= 0 || h1 <= 0) continue;

            float left1 = Math.Max(0, x1);
            float top1 = Math.Max(0, y1);
            float right1 = left1 + w1;
            float bottom1 = top1 + h1;
            float area1 = w1 * h1;

            selected.Add((new Rectangle(
                (int)left1, (int)top1, (int)(right1 - left1), (int)(bottom1 - top1)), score));

            for (int j = i + 1; j < candidates.Count; j++)
            {
                if (removed[j]) continue;

                var (x2, y2, w2, h2, _) = candidates[j];
                if (w2 <= 0 || h2 <= 0) continue;

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

   

    public void Dispose()
    {
        _session?.Dispose();
    }
}
