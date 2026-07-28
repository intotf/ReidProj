using FaceFeature.Payloads;
using System.Text.Json.Serialization;

namespace FaceFeature
{
    [JsonSerializable(typeof(IAsyncEnumerable<FaceDetection>))]
    [JsonSerializable(typeof(DetectionFlags))]
    [JsonSerializable(typeof(UrlDetectRequest))]
    [JsonSerializable(typeof(FacePerson[]))]
    [JsonSerializable(typeof(IAsyncEnumerable<FaceRecognition>))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
