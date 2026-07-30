using ReidFeature.Payloads;
using ReidFeature.Services;
using System.Text.Json.Serialization;

namespace ReidFeature
{
    [JsonSerializable(typeof(IAsyncEnumerable<PersonDetection>))]
    [JsonSerializable(typeof(DetectionFlags))]
    [JsonSerializable(typeof(PersonRecognition))]
    [JsonSerializable(typeof(EnrollResult))]
    [JsonSerializable(typeof(MemberInfo[]))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
