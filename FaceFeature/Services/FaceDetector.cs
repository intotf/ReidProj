using FaceFeature.Helpers;
using FaceFeature.Payloads;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;
using System.Diagnostics;

namespace FaceFeature.Services;

/// <summary>
/// SCRFD-10g ONNX 人脸检测器 — 基于 InsightFace buffalo_l 模型包（det_10g.onnx）。
/// 固定模型布局：3 层 FPN（stride 8/16/32）、每像素 2 anchor、channels-last、5 关键点。
/// 输入 RGB 图像，输出检测到的人脸边界框与关键点（坐标相对于原图）。
/// </summary>
public sealed class FaceDetector : IDisposable
{
    // ─── 模型常量 ─────────────────────────────────────────────

    /// <summary>SCRFD 模型期望的输入图像尺寸（宽高均为 640）</summary>
    private const int InputSize = 640;

    /// <summary>特征金字塔层数（det_10g 固定 3 层）</summary>
    private const int Fmc = 3;

    /// <summary>每像素锚点数（det_10g 固定 2）</summary>
    private const int NumAnchors = 2;

    /// <summary>各特征图的下采样步长（det_10g 固定 [8, 16, 32]）</summary>
    private static readonly int[] Strides = [8, 16, 32];

<<<<<<< HEAD
    // ─── 解码常量 ─────────────────────────────────────────────

    /// <summary>人脸置信度过滤阈值</summary>
    private const float ConfidenceThreshold = 0.6f;

    /// <summary>人脸最小尺寸（像素），低于此值的人脸特征不可靠，将被忽略</summary>
    private const int MinFaceSize = 50;

=======
>>>>>>> 0d9348b8afaaeb08c15a87e8c14b6d72371148cb
    // ─── 预处理常量（SCRFD 归一化）───────────────────────────

    /// <summary>SCRFD 预处理的像素均值</summary>
    private const float ScrfdMean = 127.5f;

    /// <summary>SCRFD 预处理的像素标准差</summary>
    private const float ScrfdStd = 128f;

    /// <summary>归一化后的黑色像素值 (0 - 127.5) / 128</summary>
    private const float BlackNorm = -ScrfdMean / ScrfdStd;

    /// <summary>归一化常数：1 / 128</summary>
    private const float InvStd = 1f / ScrfdStd;

    /// <summary>归一化常数：127.5 / 128</summary>
    private const float MeanDivStd = ScrfdMean / ScrfdStd;

    private readonly ILogger<FaceDetector> _logger;

    /// <summary>ONNX Runtime 推理会话</summary>
    private readonly InferenceSession _session;

    /// <summary>
    /// 初始化 SCRFD 人脸检测器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="onnxOptions">ONNX Runtime 会话配置</param>
    /// <exception cref="FileNotFoundException">models/det_10g.onnx 未找到时抛出</exception>
    public FaceDetector(ILogger<FaceDetector> logger, IOptions<OnnxSessionOptions> onnxOptions)
    {
        _logger = logger;
        _session = new InferenceSession(FindModelPath(), onnxOptions.Value.Face);

        // det_10g（buffalo_l）固定布局：9 个输出（3 层 score / bbox / 5 关键点），channels-last。
        // 更换模型时需同步调整 Fmc / NumAnchors / Strides 与解码逻辑。
        if (_session.OutputMetadata.Count != 9)
        {
            throw new InvalidOperationException(
                $"FaceDetector 仅支持 det_10g 固定布局（9 个输出），当前模型输出数: {_session.OutputMetadata.Count}");
        }
    }

    /// <summary>在输出目录 → 项目目录中定位模型文件</summary>
    private static string FindModelPath()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "det_10g.onnx");
        if (!File.Exists(modelPath))
        {
            modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "det_10g.onnx");
        }
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("请先将 det_10g.onnx 放到 models/ 目录下", modelPath);
        }
        return modelPath;
    }

    /// <summary>
    /// 检测图像中置信度最高的可用人脸（性能优先——单趟扫描，不构建全量候选列表）
    /// </summary>
    /// <param name="image">输入 RGB 图像</param>
    /// <returns>置信度最高的单人脸（含关键点），无人脸时返回 null</returns>
    public FaceBox? DetectBest(Image<Rgb24> image)
    {
        var sw = Stopwatch.StartNew();

        // 居中 letterbox → 640×640，黑色填充（单趟直写张量，无中间图像分配）
        float scale = Math.Min((float)InputSize / image.Width, (float)InputSize / image.Height);
        int newW = (int)(image.Width * scale);
        int newH = (int)(image.Height * scale);
        float padX = (InputSize - newW) / 2f;
        float padY = (InputSize - newH) / 2f;

        int tensorSize = 3 * InputSize * InputSize;
        float[] buffer = ArrayPool<float>.Shared.Rent(tensorSize);
        try
        {
            FillTensorLetterbox(image, scale, padX, padY, buffer.AsMemory(0, tensorSize));

            var inputName = _session.InputMetadata.Keys.First();
            var input = NamedOnnxValue.CreateFromTensor(
                inputName,
                new DenseTensor<float>(buffer.AsMemory(0, tensorSize), [1, 3, InputSize, InputSize]));
            using var results = _session.Run([input]);

            // 只保留置信度最高的候选，无需构建全量列表与 NMS
            Candidate? best = null;
            for (int level = 0; level < Fmc; level++)
            {
                int stride = Strides[level];
                int fmSize = InputSize / stride;

                var scores = ((DenseTensor<float>)results[level].AsTensor<float>()).Buffer.Span;
                var bboxes = ((DenseTensor<float>)results[Fmc + level].AsTensor<float>()).Buffer.Span;
                var kpss = ((DenseTensor<float>)results[2 * Fmc + level].AsTensor<float>()).Buffer.Span;

                DecodeLevel(
                    scores, bboxes, kpss,
                    stride, NumAnchors, fmSize,
<<<<<<< HEAD
                    scale, padX, padY, image.Width, image.Height,
=======
                    scale, padX, padY,
                    Math.Max(1, _options.MinFaceSize),
                    Math.Clamp(_options.ConfidenceThreshold, 0f, 1f),
                    image.Width, image.Height,
>>>>>>> 0d9348b8afaaeb08c15a87e8c14b6d72371148cb
                    ref best);
            }

            var face = best is { } c
                ? new FaceBox(new Rectangle((int)c.X, (int)c.Y, (int)c.W, (int)c.H), c.Score, c.Keypoints)
                : null;
            Log.FaceDetectionCompleted(_logger, face is null ? 0 : 1, sw.Elapsed.TotalMilliseconds);
            return face;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 释放 ONNX Runtime 推理会话
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
    }

    /// <summary>
    /// 单趟完成 letterbox 缩放 + 归一化：双线性采样源图并直接写入 SCRFD 的 CHW 张量，
    /// 避免每帧创建缩放图与画布等中间分配（原实现为 Lanczos3 缩放 + DrawImage + 逐像素填充）。
    /// </summary>
    /// <remarks>
    /// 归一化公式: output = (pixel - 127.5) / 128.0
    /// 输出为 CHW 连续内存布局，三个平面依次为 R→G→B 通道；letterbox 区域填黑（归一化后为 BlackNorm）。
    /// 坐标映射与 DetectBest 的逆映射一致：(src = (out - pad) / scale)，保证检测框映射回原图无偏移。
    /// </remarks>
    /// <param name="image">源图像（任意分辨率）</param>
    /// <param name="scale">letterbox 缩放比例</param>
    /// <param name="padX">letterbox 水平填充量</param>
    /// <param name="padY">letterbox 垂直填充量</param>
    /// <param name="dest">目标缓冲区，长度至少 3×640×640</param>
    private static void FillTensorLetterbox(
        Image<Rgb24> image,
        float scale,
        float padX,
        float padY,
        Memory<float> dest)
    {
        int srcW = image.Width, srcH = image.Height;
        int planeSize = InputSize * InputSize;
        float invScale = 1f / scale;

        image.ProcessPixelRows(acc =>
        {
            var destSpan = dest.Span;
            for (int y = 0; y < InputSize; y++)
            {
                int off = y * InputSize;
                float srcY = (y - padY) * invScale;
                if (srcY < 0 || srcY >= srcH)
                {
                    destSpan.Slice(off, InputSize).Fill(BlackNorm);
                    destSpan.Slice(planeSize + off, InputSize).Fill(BlackNorm);
                    destSpan.Slice(2 * planeSize + off, InputSize).Fill(BlackNorm);
                    continue;
                }

                int y0 = (int)srcY;
                int y1 = Math.Min(y0 + 1, srcH - 1);
                float fy = srcY - y0;
                float invFy = 1f - fy;
                var row0 = acc.GetRowSpan(y0);
                var row1 = acc.GetRowSpan(y1);

                for (int x = 0; x < InputSize; x++)
                {
                    float srcX = (x - padX) * invScale;
                    int i = off + x;
                    if (srcX < 0 || srcX >= srcW)
                    {
                        destSpan[i] = BlackNorm;
                        destSpan[planeSize + i] = BlackNorm;
                        destSpan[2 * planeSize + i] = BlackNorm;
                        continue;
                    }

                    int x0 = (int)srcX;
                    int x1 = Math.Min(x0 + 1, srcW - 1);
                    float fx = srcX - x0;
                    float invFx = 1f - fx;

                    // 双线性插值并归一化，RGB 三通道
                    float r = invFy * (invFx * row0[x0].R + fx * row0[x1].R)
                            + fy * (invFx * row1[x0].R + fx * row1[x1].R);
                    float g = invFy * (invFx * row0[x0].G + fx * row0[x1].G)
                            + fy * (invFx * row1[x0].G + fx * row1[x1].G);
                    float b = invFy * (invFx * row0[x0].B + fx * row0[x1].B)
                            + fy * (invFx * row1[x0].B + fx * row1[x1].B);

                    destSpan[i] = r * InvStd - MeanDivStd;
                    destSpan[planeSize + i] = g * InvStd - MeanDivStd;
                    destSpan[2 * planeSize + i] = b * InvStd - MeanDivStd;
                }
            }
        });
    }

    /// <summary>
    /// 解码单个特征图级别的 SCRFD 输出，将距离回归值转为原图坐标系下的边界框
    /// </summary>
    /// <remarks>
    /// Anchor 点定义在特征图 cell 的左上角 (x * stride, y * stride)，与 InsightFace 官方实现一致。
    /// 张量采用空间优先（spatial-major）布局：索引 = pixelIdx * numAnchors + anchorIdx，
    /// 即同一像素位置上不同 anchor 的数据连续排列。
    /// bbox 为 channels-last [N, 4]，关键点为 channels-last [N, 10]（det_10g 固定布局）。
    /// 
    /// 反向映射到原图坐标：(anchor - left/top - pad) / scale，再 clamp 到图像范围。
    /// </remarks>
    /// <param name="scores">置信度张量扁平化 span</param>
    /// <param name="bboxes">边界框回归值张量扁平化 span</param>
    /// <param name="kpss">关键点回归值张量扁平化 span（每锚点 10 个值 = 5 点 × 2 坐标）</param>
    /// <param name="stride">当前特征图的下采样步长</param>
    /// <param name="numAnchors">每像素锚点数</param>
    /// <param name="fmSize">特征图边长（fmSize × fmSize）</param>
    /// <param name="scale">letterbox 缩放比例</param>
    /// <param name="padX">letterbox 水平填充量</param>
    /// <param name="padY">letterbox 垂直填充量</param>
<<<<<<< HEAD
=======
    /// <param name="minFaceSize">人脸最小尺寸（像素），低于该值的候选框丢弃</param>
    /// <param name="confidenceThreshold">人脸置信度阈值，低于该值的候选框丢弃</param>
>>>>>>> 0d9348b8afaaeb08c15a87e8c14b6d72371148cb
    /// <param name="imgW">原图宽度</param>
    /// <param name="imgH">原图高度</param>
    /// <param name="best">当前置信度最高的候选框（尚未找到时为 null）</param>
    private static void DecodeLevel(
        ReadOnlySpan<float> scores, ReadOnlySpan<float> bboxes, ReadOnlySpan<float> kpss,
        int stride, int numAnchors, int fmSize,
<<<<<<< HEAD
        float scale, float padX, float padY,
=======
        float scale, float padX, float padY, int minFaceSize, float confidenceThreshold,
>>>>>>> 0d9348b8afaaeb08c15a87e8c14b6d72371148cb
        int imgW, int imgH,
        ref Candidate? best)
    {
        int total = fmSize * fmSize * numAnchors;

        for (int i = 0; i < total; i++)
        {
            float raw = scores[i];
            float score = 1f / (1f + MathF.Exp(-raw));
            if (score < confidenceThreshold)
            {
                continue;
            }

            // 线性索引 → 锚点索引 → 像素坐标 → anchor 中心点（单位：像素）
            int pixelIdx = Math.DivRem(i, numAnchors, out _);
            int cy = Math.DivRem(pixelIdx, fmSize, out int cx);
            float anchorX = cx * stride;
            float anchorY = cy * stride;

            // channels-last [total, 4]：同一锚点的 4 个回归值连续（det_10g 固定布局）
            int off = i * 4;
            float left = bboxes[off] * stride;
            float top = bboxes[off + 1] * stride;
            float right = bboxes[off + 2] * stride;
            float bottom = bboxes[off + 3] * stride;

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
            if (w < MinFaceSize || h < MinFaceSize)
            {
                continue;
            }

            if (best is not null && score <= best.Value.Score)
            {
                continue;
            }

            var kps = new PointF[5];
            int kpsOffset = i * 10;
            for (int k = 0; k < 5; k++)
            {
                float kx = (anchorX + kpss[kpsOffset + 2 * k] * stride - padX) / scale;
                float ky = (anchorY + kpss[kpsOffset + 2 * k + 1] * stride - padY) / scale;
                kps[k] = new PointF(Math.Clamp(kx, 0f, imgW), Math.Clamp(ky, 0f, imgH));
            }

            best = new Candidate(x1, y1, w, h, score, kps);
        }
    }

    /// <summary>
    /// 解码后的候选框值类型
    /// </summary>
    /// <param name="X">左上角 X 坐标</param>
    /// <param name="Y">左上角 Y 坐标</param>
    /// <param name="W">宽度</param>
    /// <param name="H">高度</param>
    /// <param name="Score">人脸置信度</param>
    /// <param name="Keypoints">5 个关键点（原图坐标）</param>
    private readonly record struct Candidate(float X, float Y, float W, float H, float Score, PointF[] Keypoints);
}
