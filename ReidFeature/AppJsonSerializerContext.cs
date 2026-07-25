using System.Text.Json.Serialization;
using ReidFeature.Payloads;

namespace ReidFeature
{
    [JsonSerializable(typeof(DetectResponse))]
    [JsonSerializable(typeof(PersonDetection[]))]
    [JsonSerializable(typeof(BoundingBox))]
    [JsonSerializable(typeof(FaceDetection))]
    [JsonSerializable(typeof(DetectionFlags))]
    [JsonSerializable(typeof(DetectionFlags?))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
