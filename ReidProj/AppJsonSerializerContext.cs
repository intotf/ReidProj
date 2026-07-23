using System.Text.Json.Serialization;
using ReidProj.Models;

namespace ReidProj
{
    [JsonSerializable(typeof(DetectResponse))]
    [JsonSerializable(typeof(PersonDetection[]))]
    [JsonSerializable(typeof(BoundingBox))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
