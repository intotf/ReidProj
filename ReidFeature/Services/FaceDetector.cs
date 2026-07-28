using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ReidFeature.Helpers;
using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;
using System.Diagnostics;

namespace ReidFeature.Services;

/// <summary>
/// SCRFD-10g-kps ONNX 推理 + 多级 anchor 解码 + NMS，人脸检测（含 5 点关键点）
/// </summary>
public sealed class FaceDetector : IDisposable
{
    private readonly ILogger<FaceDetector> _logger;
    private readonly InferenceSession _session;

    // 模型输入尺寸
    private const int InputSize = 640;

    // 输出数量 → (fmc, strides) 映射（InsightFace SCRFD 约定）
    private static readonly Dictionary<int, (int Fmc, int[] Strides)> OutputLayouts = new()
    {
        [6] = (3, [8, 16, 32]),
        [9] = (3, [8, 16, 32]),
        [10] = (5, [8, 16, 32, 64, 128]),
        [15] = (5, [8, 16, 32, 64, 128]),
    };

    // NMS 参数
    private const float NmsThreshold = 0.4f;
    private const float ConfidenceThreshold = 0.5f;

    /// <summary>
    /// 初始化人脸检测器，加载 SCRFD ONNX 模型
    /// </summary>
    public FaceDetector(ILogger<FaceDetector> logger, IOptions<OnnxSessionOptions> onnxOptions)
    {
        _logger = logger;

        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "scrfd_10g.onnx");
        if (!File.Exists(modelPath))
        {
            modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "scrfd_10g.onnx");
        }
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("请先将 scrfd_10g.onnx 放到 models/ 目录下，或运行 python scripts/setup_models.py 下载", modelPath);
        }

        _session = new InferenceSession(modelPath, onnxOptions.Value.Face);
    }

    /// <summary>
    /// 检测图像中的所有人脸，返回边界框列表（坐标相对于输入图像）
    /// </summary>
    public List<(Rectangle Bbox, float Confidence)> Detect(Image<Rgb24> image)
    {
        var sw = Stopwatch.StartNew();

        // 1. 保持宽高比的 letterbox 缩放至 640×640
        using var resized = ImageProcessor.LetterboxResize(image, InputSize);

        // 2. 构建 CHW 输入张量，应用 SCRFD 归一化 (x-127.5)/128
        int bufferSize = 3 * InputSize * InputSize;
        float[] pixelData = ArrayPool<float>.Shared.Rent(bufferSize);
        try
        {
            NormalizeToScrfdTensor(resized, pixelData);
            var inputTensor = new DenseTensor<float>(pixelData.AsMemory(0, bufferSize), [1, 3, InputSize, InputSize]);

            // 3. ONNX 运行时推理
            var inputName = _session.InputMetadata.Keys.First();
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(inputName, inputTensor)]);

            // 根据输出张量个数识别模型布局（6/9/10/15 输出）
            if (!OutputLayouts.TryGetValue(results.Count, out var layout))
                throw new InvalidOperationException(
                    $"Unknown SCRFD output layout: {results.Count} outputs. Names: {string.Join(", ", results.Select(r => r.Name))}");

            var (fmc, strides) = layout;

            // 4. 收集各特征图级别的候选框
            float scale = Math.Min((float)InputSize / image.Width, (float)InputSize / image.Height);
            float padX = (InputSize - (int)(image.Width * scale)) / 2f;
            float padY = (InputSize - (int)(image.Height * scale)) / 2f;

            var candidates = new List<(float X, float Y, float W, float H, float Score)>();

            for (int level = 0; level < fmc; level++)
            {
                // 张量布局: [总锚点数, 通道数]
                var scoreTensor = (DenseTensor<float>)results[level].AsTensor<float>();
                var bboxTensor = (DenseTensor<float>)results[fmc + level].AsTensor<float>();

                // 由 stride 推算特征图尺寸，由总行数反推每像素锚点数
                int stride = strides[level];
                int fmSize = InputSize / stride;
                int totalAnchors = (int)scoreTensor.Dimensions[0];
                int planeSize = fmSize * fmSize;
                int derivedAnchors = totalAnchors / planeSize;

                DecodeLevel(scoreTensor, bboxTensor,
                    stride, derivedAnchors, fmSize, fmSize,
                    scale, padX, padY, image.Width, image.Height, candidates);
            }

            // 5. 非极大值抑制（NMS）
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
    /// 检测置信度最高的人脸（适用于裁剪后的人物区域）
    /// </summary>
    /// <param name="image">输入 RGB 图像</param>
    /// <param name="offsetX">在父图中的 X 偏移量</param>
    /// <param name="offsetY">在父图中的 Y 偏移量</param>
    /// <returns>人脸检测结果（坐标已偏移），无人脸时返回 null</returns>
    public FaceDetection? DetectBestFace(Image<Rgb24> image, int offsetX, int offsetY)
    {
        var faces = Detect(image);
        if (faces.Count == 0)
            return null;

        var best = faces.MaxBy(f => f.Confidence);
        return new FaceDetection(
            new BoundingBox(offsetX + best.Bbox.X, offsetY + best.Bbox.Y,
                best.Bbox.Width, best.Bbox.Height),
            best.Confidence
        );
    }

    /// <summary>
    /// 解码单个特征图级别：将距离值转为原图坐标
    /// </summary>
    /// <param name="scoreTensor">置信度张量 [总锚点数, 1]</param>
    /// <param name="bboxTensor">边界框回归张量 [总锚点数, 4] (l,t,r,b 距离)</param>
    /// <param name="stride">当前特征图下采样步长</param>
    /// <param name="numAnchors">每像素锚点数</param>
    /// <param name="fmH">特征图高度</param>
    /// <param name="fmW">特征图宽度</param>
    /// <param name="scale">letterbox 缩放比例</param>
    /// <param name="padX">letterbox 水平填充</param>
    /// <param name="padY">letterbox 垂直填充</param>
    /// <param name="imgW">原图宽度</param>
    /// <param name="imgH">原图高度</param>
    /// <param name="candidates">候选框列表（输出）</param>
    private static void DecodeLevel(
        DenseTensor<float> scoreTensor,
        DenseTensor<float> bboxTensor,
        int stride, int numAnchors, int fmH, int fmW,
        float scale, float padX, float padY,
        int imgW, int imgH,
        List<(float X, float Y, float W, float H, float Score)> candidates)
    {
        var scores = scoreTensor.Buffer.Span;
        var bboxes = bboxTensor.Buffer.Span;

        // 2D 扁平化 tensor: scores[N,1], bboxes[N,4]
        int bboxChannels = (int)bboxTensor.Dimensions[1];
        int planeSize = fmH * fmW;

        for (int y = 0; y < fmH; y++)
        {
            for (int x = 0; x < fmW; x++)
            {
                float cx = (x + 0.5f) * stride;
                float cy = (y + 0.5f) * stride;

                int pixelIdx = y * fmW + x;

                for (int a = 0; a < numAnchors; a++)
                {
                    int rowIdx = a * planeSize + pixelIdx;
                    float rawScore = scores[rowIdx];
                    float score = 1f / (1f + MathF.Exp(-rawScore));
                    if (score < ConfidenceThreshold)
                        continue;

                    // 2D 扁平化: row_major → index = rowIdx * bboxChannels + c
                    int bboxRow = rowIdx * bboxChannels;
                    float left = bboxes[bboxRow + 0] * stride;
                    float top = bboxes[bboxRow + 1] * stride;
                    float right = bboxes[bboxRow + 2] * stride;
                    float bottom = bboxes[bboxRow + 3] * stride;

                    float x1 = cx - left;
                    float y1 = cy - top;
                    float x2 = cx + right;
                    float y2 = cy + bottom;

                    float rx1 = Math.Clamp((x1 - padX) / scale, 0f, imgW);
                    float ry1 = Math.Clamp((y1 - padY) / scale, 0f, imgH);
                    float rx2 = Math.Clamp((x2 - padX) / scale, 0f, imgW);
                    float ry2 = Math.Clamp((y2 - padY) / scale, 0f, imgH);

                    float w = rx2 - rx1;
                    float h = ry2 - ry1;
                    if (w <= 0 || h <= 0)
                        continue;

                    candidates.Add((rx1, ry1, w, h, score));
                }
            }
        }
    }

    /// <summary>
    /// 非极大值抑制（贪心 NMS，按置信度降序处理）
    /// </summary>
    /// <param name="candidates">候选框列表 (x, y, w, h, score)</param>
    /// <returns>抑制后保留的边界框列表</returns>
    private static List<(Rectangle Bbox, float Confidence)> Nms(
        List<(float X, float Y, float W, float H, float Score)> candidates)
    {
        var selected = new List<(Rectangle Bbox, float Confidence)>();
        if (candidates.Count == 0)
            return selected;

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        int count = candidates.Count;
        var removed = new bool[count];

        for (int i = 0; i < count; i++)
        {
            if (removed[i]) continue;

            var (x1, y1, w1, h1, score) = candidates[i];
            float area1 = w1 * h1;

            selected.Add((new Rectangle((int)x1, (int)y1, (int)w1, (int)h1), score));

            for (int j = i + 1; j < count; j++)
            {
                if (removed[j]) continue;

                var (x2, y2, w2, h2, _) = candidates[j];
                float interLeft = Math.Max(x1, x2);
                float interTop = Math.Max(y1, y2);
                float interRight = Math.Min(x1 + w1, x2 + w2);
                float interBottom = Math.Min(y1 + h1, y2 + h2);

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

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
    }


    /// <summary>
    /// 将 ImageSharp 图像转为 SCRFD 归一化后的 CHW 张量
    /// </summary>
    /// <param name="image">输入 RGB 图像（640×640）</param>
    /// <param name="destination">输出扁平化数组，按 R/G/B 通道连续排列</param>
    private static void NormalizeToScrfdTensor(Image<Rgb24> image, float[] destination)
    {
        const float ScrfdMean = 127.5f;
        const float ScrfdStd = 128.0f;

        int h = image.Height, w = image.Width;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    int idx = y * w + x;
                    destination[idx] = (p.R - ScrfdMean) / ScrfdStd;
                    destination[h * w + idx] = (p.G - ScrfdMean) / ScrfdStd;
                    destination[2 * h * w + idx] = (p.B - ScrfdMean) / ScrfdStd;
                }
            }
        });
    }
}
