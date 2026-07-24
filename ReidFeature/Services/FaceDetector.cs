using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ReidFeature.Helpers;
using ReidFeature.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;
using System.Diagnostics;

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

    public FaceDetector(ILogger<FaceDetector> logger, IOptions<OnnxSessionOptions> onnxOptions)
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

        Log.LoadingFaceModel(_logger, modelPath);
        _session = new InferenceSession(modelPath, onnxOptions.Value.Face);
        Log.FaceModelLoaded(_logger);
    }

    /// <summary>
    /// 检测图像中的人脸，返回边界框列表（坐标相对于输入图像）
    /// </summary>
    public List<(Rectangle Bbox, float Confidence)> Detect(Image<Rgb24> image)
    {
        var sw = Stopwatch.StartNew();

        // 1. Letterbox resize
        using var resized = ImageProcessor.LetterboxResize(image, InputSize);

        // 2. 构建 CHW tensor (3×640×640)
        int bufferSize = 3 * InputSize * InputSize;
        float[] pixelData = ArrayPool<float>.Shared.Rent(bufferSize);
        try
        {
            ImageProcessor.NormalizeToTensor(resized, pixelData);
            var inputTensor = new DenseTensor<float>(pixelData.AsMemory(0, bufferSize), [1, 3, InputSize, InputSize]);

            // 3. ONNX 推理
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor("images", inputTensor)]);

            // 4. 解析输出
            var outputTensor = (DenseTensor<float>)results[0].AsTensor<float>();
            var outputSpan = outputTensor.Buffer.Span;
            var dims = outputTensor.Dimensions;
            int numDetections = dims[2];
            int stride = numDetections;

            // 5. 框解码 + 置信度过滤
            var candidates = new List<(float X, float Y, float W, float H, float Score)>(numDetections);

            float scale = Math.Min((float)InputSize / image.Width, (float)InputSize / image.Height);
            float padX = (InputSize - (int)(image.Width * scale)) / 2f;
            float padY = (InputSize - (int)(image.Height * scale)) / 2f;

            for (int i = 0; i < numDetections; i++)
            {
                // 人脸只有类别 0，直接读取分数
                float score = outputSpan[4 * stride + i];
                if (score < ConfidenceThreshold)
                    continue;

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

                candidates.Add((x1, y1, boxW, boxH, score));
            }

            // 6. NMS
            var resultsList = Nms(candidates);

            Log.FaceDetectionCompleted(_logger, resultsList.Count, sw.Elapsed.TotalMilliseconds);
            return resultsList;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(pixelData);
        }
    }

    /// <summary>
    /// 检测图像中的最佳人脸，返回映射到原始图像坐标的 FaceDetection
    /// </summary>
    public FaceDetection? DetectBestFace(Image<Rgb24> image, int offsetX, int offsetY)
    {
        var sw = Stopwatch.StartNew();

        // 1. Letterbox resize
        using var resized = ImageProcessor.LetterboxResize(image, InputSize);

        // 2. 构建 CHW tensor
        int bufferSize = 3 * InputSize * InputSize;
        float[] pixelData = ArrayPool<float>.Shared.Rent(bufferSize);
        try
        {
            ImageProcessor.NormalizeToTensor(resized, pixelData);
            var inputTensor = new DenseTensor<float>(pixelData.AsMemory(0, bufferSize), [1, 3, InputSize, InputSize]);

            // 3. ONNX 推理
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor("images", inputTensor)]);

            // 4. 在原始输出中找最高分人脸（跳过 NMS）
            var outputTensor = (DenseTensor<float>)results[0].AsTensor<float>();
            var outputSpan = outputTensor.Buffer.Span;
            int numDetections = outputTensor.Dimensions[2];
            int stride = numDetections;

            float scale = Math.Min((float)InputSize / image.Width, (float)InputSize / image.Height);
            float padX = (InputSize - (int)(image.Width * scale)) / 2f;
            float padY = (InputSize - (int)(image.Height * scale)) / 2f;

            float bestScore = 0;
            float bestX = 0, bestY = 0, bestW = 0, bestH = 0;

            for (int i = 0; i < numDetections; i++)
            {
                float score = outputSpan[4 * stride + i];
                if (score <= bestScore)
                    continue;

                // 反 letterbox + clamp 只对当前候选框做
                float cx = outputSpan[0 * stride + i];
                float cy = outputSpan[1 * stride + i];
                float bw = outputSpan[2 * stride + i];
                float bh = outputSpan[3 * stride + i];

                float x1 = Math.Clamp((cx - bw / 2f - padX) / scale, 0f, image.Width);
                float y1 = Math.Clamp((cy - bh / 2f - padY) / scale, 0f, image.Height);
                float x2 = Math.Clamp((cx + bw / 2f - padX) / scale, 0f, image.Width);
                float y2 = Math.Clamp((cy + bh / 2f - padY) / scale, 0f, image.Height);

                bestScore = score;
                bestX = x1;
                bestY = y1;
                bestW = Math.Max(0, x2 - x1);
                bestH = Math.Max(0, y2 - y1);
            }

            if (bestScore < ConfidenceThreshold)
            {
                Log.BestFaceNotFound(_logger, sw.Elapsed.TotalMilliseconds);
                return null;
            }

            Log.BestFaceDetected(_logger, bestScore, sw.Elapsed.TotalMilliseconds);
            return new FaceDetection(
                new BoundingBox(
                    offsetX + (int)bestX,
                    offsetY + (int)bestY,
                    (int)bestW,
                    (int)bestH
                ),
                bestScore
            );
        }
        finally
        {
            ArrayPool<float>.Shared.Return(pixelData);
        }
    }

    private static List<(Rectangle Bbox, float Confidence)> Nms(List<(float X, float Y, float W, float H, float Score)> candidates)
    {
        var selected = new List<(Rectangle Bbox, float Confidence)>();
        if (candidates.Count == 0)
            return selected;

        // 按置信度降序排序
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        int count = candidates.Count;
        var removed = new bool[count];

        for (int i = 0; i < count; i++)
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

            for (int j = i + 1; j < count; j++)
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

    public void Dispose()
    {
        _session?.Dispose();
    }
}
