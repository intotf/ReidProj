namespace ReidFeature.Payloads;

/// <summary>
/// 单个人物的检测+特征结果
/// </summary>
/// <param name="Bbox">人物边界框（原图坐标）</param>
/// <param name="Confidence">YOLO 检测置信度</param>
/// <param name="Features">全身 ReID 特征向量（原始字节）</param>
/// <param name="FeaturePack">可选的四维特征包（含头肩 ReID、体型、步态），视频流模式下填充</param>
/// <param name="TrackId">ByteTrack Track ID（用于识别结果溯源）</param>
public sealed record PersonDetection(
    BoundingBox Bbox,
    float Confidence,
    byte[] Features,
    TrackFeaturePack? FeaturePack = null,
    int TrackId = 0
);
