using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ReidFeature.Services;

/// <summary>
/// 检测编排服务：YOLO 人物检测 → ReID 特征提取 → 人脸检测（可选）
/// </summary>
public sealed class DetectService
{
    private readonly YoloDetector _yolo;
    private readonly ReIdExtractor _reid;
    private readonly FaceDetector _faceDetector;

    /// <summary>
    /// 检测编排服务
    /// </summary>
    /// <param name="yolo"></param>
    /// <param name="reid"></param>
    /// <param name="faceDetector"></param>
    public DetectService(YoloDetector yolo, ReIdExtractor reid, FaceDetector faceDetector)
    {
        _yolo = yolo;
        _reid = reid;
        _faceDetector = faceDetector;
    }

    /// <summary>
    /// 对输入图像执行完整检测管线
    /// </summary>
    /// <param name="image">输入 RGB 图像</param>
    /// <param name="flags">检测功能标志位</param>
    /// <returns>检测到的人物列表（可能为空）</returns>
    public PersonDetection[] Detect(Image<Rgb24> image, DetectionFlags? flags)
    {
        var detections = _yolo.DetectPersons(image);
        if (detections.Count == 0)
        {
            return [];
        }

        var persons = new PersonDetection[detections.Count];
        for (int i = 0; i < detections.Count; i++)
        {
            var (box, conf) = detections[i];
            int x = Math.Clamp(box.X, 0, image.Width - 1);
            int y = Math.Clamp(box.Y, 0, image.Height - 1);
            int w = Math.Max(1, Math.Min(box.Width, image.Width - x));
            int h = Math.Max(1, Math.Min(box.Height, image.Height - y));

            using var cropped = image.Clone(ctx => ctx.Crop(new Rectangle(x, y, w, h)));

            var features = _reid.ExtractFeatures(cropped);

            FaceDetection? face = null;
            if (flags?.HasFlag(DetectionFlags.SkipFaceDetection) != true)
                face = _faceDetector.DetectBestFace(cropped, box.X, box.Y);

            persons[i] = new PersonDetection(
                Bbox: new BoundingBox(box.X, box.Y, box.Width, box.Height),
                Confidence: conf,
                Features: features,
                Face: face);
        }

        return persons;
    }
}
