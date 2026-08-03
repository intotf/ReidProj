namespace FaceFeature.Payloads;

/// <summary>
/// index.json 中持久化的人脸记录（特征以 base64 存储）
/// </summary>
internal sealed class PersistedFace
{
    public string Id { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public float Sharpness { get; set; }
    public int BboxX { get; set; }
    public int BboxY { get; set; }
    public int BboxWidth { get; set; }
    public int BboxHeight { get; set; }
    public DateTime RegisteredAt { get; set; }
    public byte[] FaceFeatures { get; set; } = [];
    public string ImageFile { get; set; } = string.Empty;
}
