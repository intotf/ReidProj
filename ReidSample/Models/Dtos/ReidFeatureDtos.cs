using System.Text.Json.Serialization;

namespace ReIdSample.Models.Dtos;

// ReidFeature 检测响应反序列化用
public class ReidDetectResponse
{
    [JsonPropertyName("persons")]
    public List<ReidPersonDetection> Persons { get; set; } = [];
}

public class ReidPersonDetection
{
    [JsonPropertyName("bbox")]
    public ReidBoundingBox Bbox { get; set; } = null!;

    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    [JsonPropertyName("features")]
    public byte[] Features { get; set; } = [];
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
