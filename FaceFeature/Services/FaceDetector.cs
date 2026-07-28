using FaceFeature.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Diagnostics;

namespace FaceFeature.Services;

/// <summary>
/// SCRFD-10g ONNX 人脸检测器 — 基于 InsightFace buffalo_l 模型包，支持多级 anchor 解码与 NMS。
/// 输入 RGB 图像，输出检测到的人脸边界框列表（坐标相对于原图）。
/// </summary>
public sealed class FaceDetector : IDisposable
{
    /// <summary>SCRFD 模型期望的输入图像尺寸（宽高均为 640）</summary>
    private const int InputSize = 640;

    /// <summary>NMS 去重的 IoU 阈值</summary>
    private const float NmsThreshold = 0.4f;

    /// <summary>人脸置信度过滤阈值</summary>
    private const float ConfidenceThreshold = 0.6f;

    /// <summary>人脸最小尺寸（像素），低于此值的人脸特征不可靠，将被忽略</summary>
    private const int MinFaceSize = 50;

    /// <summary>SCRFD 预处理的像素均值</summary>
    private const float ScrfdMean = 127.5f;

    /// <summary>SCRFD 预处理的像素标准差</summary>
    private const float ScrfdStd = 128f;

    /// <summary>Letterbox 填充色（黑色）</summary>
    private static readonly Rgb24 PadColor = new(0, 0, 0);

    private readonly ILogger<FaceDetector> _logger;

    /// <summary>ONNX Runtime 推理会话</summary>
    private readonly InferenceSession _session;


    /// <summary>特征金字塔的层数（3 或 5），由模型输出的张量数量决定</summary>
    private readonly int _fmc;

    /// <summary>每像素锚点数（2 或 1），由模型输出的张量数量决定</summary>
    private readonly int _numAnchors;

    /// <summary>各特征图的下采样步长（如 [8, 16, 32]）</summary>
    private readonly int[] _strides;

    /// <summary>
    /// 初始化 SCRFD 人脸检测器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="onnxOptions">ONNX Runtime 会话配置</param>
    /// <exception cref="FileNotFoundException">models/scrfd_10g.onnx 未找到时抛出</exception>
    public FaceDetector(ILogger<FaceDetector> logger, IOptions<OnnxSessionOptions> onnxOptions)
    {
        _logger = logger;

        // 搜索模型文件路径（输出目录 → 项目目录）
        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "scrfd_10g.onnx");
        if (!File.Exists(modelPath))
            modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "scrfd_10g.onnx");
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("请先将 scrfd_10g.onnx 放到 models/ 目录下", modelPath);

        _session = new InferenceSession(modelPath, onnxOptions.Value.Face);

        // 根据输出张量数量自动推断模型布局
        //   6 输出 → 3 层 + 无关键点，每像素 2 锚点（scrfd_10g）
        //   9 输出 → 3 层 + 5 关键点，每像素 2 锚点
        //  10 输出 → 5 层 + 无关键点，每像素 1 锚点
        //  15 输出 → 5 层 + 5 关键点，每像素 1 锚点
        (_fmc, _strides, _numAnchors) = _session.OutputMetadata.Count switch
        {
            6 => (3, new[] { 8, 16, 32 }, 2),
            9 => (3, [8, 16, 32], 2),
            10 => (5, [8, 16, 32, 64, 128], 1),
            15 => (5, [8, 16, 32, 64, 128], 1),
            _ => throw new InvalidOperationException($"不支持的 SCRFD 输出数量: {_session.OutputMetadata.Count}")
        };
    }

    /// <summary>
    /// 检测图像中的所有人脸
    /// </summary>
    /// <param name="image">输入 RGB 图像</param>
    /// <returns>人脸边界框列表（坐标相对于原图），无人脸时返回空列表</returns>
    public List<(Rectangle Bbox, float Confidence)> Detect(Image<Rgb24> image)
    {
        var sw = Stopwatch.StartNew();

        // 居中 letterbox → 640×640，黑色填充
        float scale = Math.Min((float)InputSize / image.Width, (float)InputSize / image.Height);
        int newW = (int)(image.Width * scale);
        int newH = (int)(image.Height * scale);
        float padX = (InputSize - newW) / 2f;
        float padY = (InputSize - newH) / 2f;

        using var resized = image.Clone(ctx => ctx.Resize(newW, newH, KnownResamplers.Bicubic));
        using var canvas = new Image<Rgb24>(InputSize, InputSize, PadColor);
        canvas.Mutate(ctx => ctx.DrawImage(resized, new Point((int)padX, (int)padY), 1f));

        int tensorSize = 3 * InputSize * InputSize;
        float[] buffer = ArrayPool<float>.Shared.Rent(tensorSize);
        try
        {
            FillTensor(canvas, buffer);

            var inputName = _session.InputMetadata.Keys.First();
            var input = NamedOnnxValue.CreateFromTensor(inputName,
                new DenseTensor<float>(buffer.AsMemory(0, tensorSize), [1, 3, InputSize, InputSize]));

            using var results = _session.Run([input]);

            bool bboxChannelsFirst = IsChannelsFirst(results[_fmc]);

            var candidates = new List<Candidate>(capacity: 200);
            for (int level = 0; level < _fmc; level++)
            {
                int stride = _strides[level];
                int fmSize = InputSize / stride;
                DecodeLevel(
                    ((DenseTensor<float>)results[level].AsTensor<float>()).Buffer.Span,
                    ((DenseTensor<float>)results[_fmc + level].AsTensor<float>()).Buffer.Span,
                    stride, _numAnchors, fmSize,
                    bboxChannelsFirst,
                    scale, padX, padY, image.Width, image.Height,
                    candidates);
            }

            var detections = Nms(candidates);
            // 过滤太小的人脸（特征不可靠）
            detections.RemoveAll(d => d.Bbox.Width < MinFaceSize || d.Bbox.Height < MinFaceSize);
            Log.FaceDetectionCompleted(_logger, detections.Count, sw.Elapsed.TotalMilliseconds);
            return detections;
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

    /// <summary>
    /// 将 640×640 RGB 图像填充为 SCRFD 归一化的 CHW 张量
    /// </summary>
    /// <remarks>
    /// 归一化公式: output = (pixel - 127.5) / 128.0
    /// 输出为 CHW 连续内存布局，三个平面依次为 R→G→B 通道。
    /// </remarks>
    /// <param name="image">640×640 的 letterbox 图像</param>
    /// <param name="dest">预分配的 float 数组，长度至少 3×640×640</param>
    private static void FillTensor(Image<Rgb24> image, float[] dest)
    {
        int h = image.Height, w = image.Width;
        int planeSize = h * w;
        image.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = acc.GetRowSpan(y);
                int rowOff = y * w;
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    int i = rowOff + x;
                    dest[i] = (p.R - ScrfdMean) / ScrfdStd;
                    dest[planeSize + i] = (p.G - ScrfdMean) / ScrfdStd;
                    dest[2 * planeSize + i] = (p.B - ScrfdMean) / ScrfdStd;
                }
            }
        });
    }

    /// <summary>
    /// 检测 bbox 输出张量的布局：channels-first ([4, N] / [1, 4, N]) 或 channels-last ([N, 4] / [1, N, 4])
    /// </summary>
    /// <remarks>
    /// 通过判断倒数第二维是否为 4 来区分两种布局：
    ///   dims[^2] == 4 → channels-first（常见的 ONNX 转出格式）
    ///   否则 → channels-last（常见的 PyTorch 转出格式）
    /// </remarks>
    /// <param name="bboxOutput">bbox 输出节点</param>
    /// <returns>true 表示 channels-first，false 表示 channels-last</returns>
    private static bool IsChannelsFirst(NamedOnnxValue bboxOutput)
    {
        var tensor = (DenseTensor<float>)bboxOutput.AsTensor<float>();
        var dims = tensor.Dimensions;
        return dims.Length >= 2 && dims[^2] == 4;
    }

    /// <summary>
    /// 解码单个特征图级别的 SCRFD 输出，将距离回归值转为原图坐标系下的边界框
    /// </summary>
    /// <remarks>
    /// Anchor 点定义在特征图 cell 的左上角 (x * stride, y * stride)，与 InsightFace 官方实现一致。
    /// 张量采用空间优先（spatial-major）布局：索引 = pixelIdx * numAnchors + anchorIdx，
    /// 即同一像素位置上不同 anchor 的数据连续排列。
    /// 
    /// 反向映射到原图坐标：(anchor - left/top - pad) / scale，再 clamp 到图像范围。
    /// </remarks>
    /// <param name="scores">置信度张量扁平化 span</param>
    /// <param name="bboxes">边界框回归值张量扁平化 span</param>
    /// <param name="stride">当前特征图的下采样步长</param>
    /// <param name="numAnchors">每像素锚点数</param>
    /// <param name="fmSize">特征图边长（fmSize × fmSize）</param>
    /// <param name="bboxChannelsFirst">bbox 是否为 channels-first 布局</param>
    /// <param name="scale">letterbox 缩放比例</param>
    /// <param name="padX">letterbox 水平填充量</param>
    /// <param name="padY">letterbox 垂直填充量</param>
    /// <param name="imgW">原图宽度</param>
    /// <param name="imgH">原图高度</param>
    /// <param name="candidates">候选框输出列表</param>
    private static void DecodeLevel(
        ReadOnlySpan<float> scores, ReadOnlySpan<float> bboxes,
        int stride, int numAnchors, int fmSize,
        bool bboxChannelsFirst,
        float scale, float padX, float padY,
        int imgW, int imgH,
        List<Candidate> candidates)
    {
        int total = fmSize * fmSize * numAnchors;

        for (int i = 0; i < total; i++)
        {
            float raw = scores[i];
            float score = 1f / (1f + MathF.Exp(-raw));
            if (score < ConfidenceThreshold)
                continue;

            // 线性索引 → 锚点索引 → 像素坐标 → anchor 中心点（单位：像素）
            int pixelIdx = Math.DivRem(i, numAnchors, out _);
            int cy = Math.DivRem(pixelIdx, fmSize, out int cx);
            float anchorX = cx * stride;
            float anchorY = cy * stride;

            float left, top, right, bottom;
            if (bboxChannelsFirst)
            {
                // channels-first [4, total]：同一通道的所有锚点数据连续
                left = bboxes[i + 0 * total] * stride;
                top = bboxes[i + 1 * total] * stride;
                right = bboxes[i + 2 * total] * stride;
                bottom = bboxes[i + 3 * total] * stride;
            }
            else
            {
                // channels-last [total, 4]：同一锚点的 4 个回归值连续
                int off = i * 4;
                left = bboxes[off] * stride;
                top = bboxes[off + 1] * stride;
                right = bboxes[off + 2] * stride;
                bottom = bboxes[off + 3] * stride;
            }

            float x1 = (anchorX - left - padX) / scale;
            float y1 = (anchorY - top - padY) / scale;
            float x2 = (anchorX + right - padX) / scale;
            float y2 = (anchorY + bottom - padY) / scale;

            x1 = Math.Clamp(x1, 0f, imgW);
            y1 = Math.Clamp(y1, 0f, imgH);
            x2 = Math.Clamp(x2, 0f, imgW);
            y2 = Math.Clamp(y2, 0f, imgH);

            float w = x2 - x1;
            float h = y2 - y1;
            if (w > 0 && h > 0)
                candidates.Add(new Candidate(x1, y1, w, h, score));
        }
    }

    // ─── NMS ──────────────────────────────────────────────────

    private static List<(Rectangle Bbox, float Confidence)> Nms(List<Candidate> cs)
    {
        var result = new List<(Rectangle, float)>();
        if (cs.Count == 0) return result;

        cs.Sort((a, b) => b.Score.CompareTo(a.Score));

        int n = cs.Count;
        var suppressed = new bool[n];

        for (int i = 0; i < n; i++)
        {
            if (suppressed[i]) continue;
            var c = cs[i];
            result.Add((new Rectangle((int)c.X, (int)c.Y, (int)c.W, (int)c.H), c.Score));

            float areaI = c.W * c.H;
            for (int j = i + 1; j < n; j++)
            {
                if (suppressed[j]) continue;
                var d = cs[j];

                float ix = Math.Max(c.X, d.X);
                float iy = Math.Max(c.Y, d.Y);
                float iw = Math.Min(c.X + c.W, d.X + d.W) - ix;
                float ih = Math.Min(c.Y + c.H, d.Y + d.H) - iy;

                if (iw <= 0 || ih <= 0) continue;

                float inter = iw * ih;
                float iou = inter / (areaI + d.W * d.H - inter);
                if (iou > NmsThreshold)
                    suppressed[j] = true;
            }
        }

        return result;
    }

    /// <summary>
    /// 解码后的候选框值类型
    /// </summary>
    /// <param name="X">左上角 X 坐标</param>
    /// <param name="Y">左上角 Y 坐标</param>
    /// <param name="W">宽度</param>
    /// <param name="H">高度</param>
    /// <param name="Score">人脸置信度</param>
    private readonly record struct Candidate(float X, float Y, float W, float H, float Score)
        : IComparable<Candidate>
    {
        /// <summary>按置信度降序比较（用于 NMS 排序）</summary>
        public int CompareTo(Candidate other) => other.Score.CompareTo(Score);
    }
}
