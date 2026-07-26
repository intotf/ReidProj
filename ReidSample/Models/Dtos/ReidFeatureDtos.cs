using System.Text.Json.Serialization;

namespace ReIdSample.Models.Dtos;

public class ReidPersonDetection
{
    [JsonPropertyName("frameIndex")]
    public int FrameIndex { get; set; }

    [JsonPropertyName("bbox")]
    public ReidBoundingBox Bbox { get; set; } = null!;

    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    [JsonPropertyName("features")]
    public byte[] Features { get; set; } = [];

    [JsonPropertyName("face")]
    public ReidFaceDetection? Face { get; set; }
}

public class ReidFaceDetection
{
    [JsonPropertyName("bbox")]
    public ReidBoundingBox Bbox { get; set; } = null!;

    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }
}

public class ReidBoundingBox
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

/// <summary>
/// 检测功能开关标志（可组合），对应服务端 ReidFeature.Payloads.DetectionFlags
/// </summary>
[Flags]
public enum DetectionFlags
{
    /// <summary>全部开启</summary>
    All = 0,
    /// <summary>跳过人脸检测</summary>
    SkipFaceDetection = 0x1,
    /// <summary>视频帧首次检测到目标后立即停止处理后续帧（仅支持流式视频端点）</summary>
    StopOnFirstFrameHit = 0x2,
}
