using System.Text.Json.Serialization;
using ReidFeature.Models;

namespace ReidFeature
{
    [JsonSerializable(typeof(DetectResponse))]
    [JsonSerializable(typeof(PersonDetection[]))]
    [JsonSerializable(typeof(BoundingBox))]
    [JsonSerializable(typeof(FaceDetection))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
