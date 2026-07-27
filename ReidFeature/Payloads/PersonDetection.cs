namespace ReidFeature.Payloads;

/// <summary>
/// 单个人物的检测+特征结果
/// </summary>
/// <param name="FrameIndex">帧索引（视频场景下表示当前帧序号，从 0 开始；非视频场景始终为 0）</param>
/// <param name="Bbox">人物边界框（原图坐标）</param>
/// <param name="Confidence">YOLO 检测置信度</param>
/// <param name="Features">ReID 特征向量（原始字节）</param>
/// <param name="Face">可选的人脸检测结果，当跳过或未检测到时为 null</param>
public sealed record PersonDetection(
    int FrameIndex,
    BoundingBox Bbox,
    float Confidence,
    byte[] Features,
    FaceDetection? Face = null
);
