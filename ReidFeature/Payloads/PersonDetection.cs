namespace ReidFeature.Payloads;

/// <summary>
/// 单个人物的检测+特征结果
/// </summary>
/// <param name="Bbox">人物边界框（原图坐标）</param>
/// <param name="Confidence">YOLO 检测置信度</param>
/// <param name="Features">ReID 特征向量（原始字节）</param>
/// <param name="Face">可选的人脸检测结果，当跳过或未检测到时为 null</param>
public sealed record PersonDetection(
    BoundingBox Bbox,
    float Confidence,
    byte[] Features,
    FaceDetection? Face = null
);
