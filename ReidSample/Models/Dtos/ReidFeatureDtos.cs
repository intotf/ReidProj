using System.Text.Json.Serialization;

namespace ReIdSample.Models.Dtos;

public class ReidPersonDetection
{
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
/// 视频编码格式，对应服务端 ReidFeature.Payloads.VideoCodec
/// </summary>
public enum VideoCodec
{
    /// <summary>原始 H264 裸流（Annex B）</summary>
    H264,
    /// <summary>原始 H265/HEVC 裸流</summary>
    H265,
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
}
