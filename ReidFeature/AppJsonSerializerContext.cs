using ReidFeature.Payloads;
using System.Text.Json.Serialization;

namespace ReidFeature
{
    [JsonSerializable(typeof(IAsyncEnumerable<PersonDetection>))]
    [JsonSerializable(typeof(DetectionFlags))]
    [JsonSerializable(typeof(UrlDetectRequest))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
