using System.Text.Json.Serialization;
using ReidFeature.Models;

namespace ReidFeature
{
    [JsonSerializable(typeof(DetectResponse))]
    [JsonSerializable(typeof(PersonDetection[]))]
    [JsonSerializable(typeof(BoundingBox))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
