using System.Text.Json.Serialization;

namespace ReIdCli;

/// <summary>
/// 缓存在内存中的目标人物特征
/// </summary>
public class TargetPerson
{
    public required string Name { get; set; }
    public required string ImagePath { get; set; }
    public required byte[] Features { get; set; }
    public float Confidence { get; set; }
}

/// <summary>
/// 匹配结果
/// </summary>
public class MatchResult
{
    public required string TargetName { get; set; }
    public required string VideoPath { get; set; }
    public required string FramePath { get; set; }
    public float Similarity { get; set; }
}

/// <summary>
/// /detect 接口返回的检测结果
/// </summary>
public class DetectResponse
{
    public List<PersonDetection> Persons { get; set; } = [];
}

public class PersonDetection
{
    public BoundingBox Bbox { get; set; } = null!;
    public float Confidence { get; set; }
    public byte[] Features { get; set; } = [];
}

public class BoundingBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// AOT 兼容的 JSON 序列化上下文
/// </summary>
[JsonSerializable(typeof(DetectResponse))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class AppJsonContext : JsonSerializerContext;
