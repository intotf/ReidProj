using System.Text.Json.Serialization;
using ReidFeature.Payloads;

namespace ReidFeature
{ 
    [JsonSerializable(typeof(IAsyncEnumerable<PersonDetection>))] 
    [JsonSerializable(typeof(Nullable<DetectionFlags>))]
    [JsonSerializable(typeof(UrlDetectRequest))]
    [JsonSerializable(typeof(VideoCodec))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
