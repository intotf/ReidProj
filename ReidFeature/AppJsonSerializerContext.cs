using ReidFeature.Payloads;
using ReidFeature.Services;
using System.Text.Json.Serialization;

namespace ReidFeature
{
    [JsonSerializable(typeof(IAsyncEnumerable<PersonDetection>))]
    [JsonSerializable(typeof(DetectionFlags))]
    [JsonSerializable(typeof(UrlDetectRequest))]
    [JsonSerializable(typeof(Person[]))]
    [JsonSerializable(typeof(Dictionary<string, byte[]>))]
    [JsonSerializable(typeof(IAsyncEnumerable<PersonRecognition>))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
