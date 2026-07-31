using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReIdFaceBox.Models;

public class PersonDetection
{
    public int FrameIndex { get; set; }
    public BoundingBox Bbox { get; set; } = null!;
    public float Confidence { get; set; }
    public string Features { get; set; } = "";
    public FaceDetection? Face { get; set; }
}

public class BoundingBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class FaceDetection
{
    public BoundingBox Bbox { get; set; } = null!;
    public float Confidence { get; set; }
    public string Features { get; set; } = "";
}

public class PersonRecognition
{
    public string Id { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Name { get; set; } = "";
    public float FaceSimilarity { get; set; }
    public float ReidSimilarity { get; set; }
}

[JsonSerializable(typeof(List<PersonDetection>))]
[JsonSerializable(typeof(List<PersonRecognition>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class DetectJsonContext : JsonSerializerContext;
