using FaceFeature.Payloads;
using System.Text.Json.Serialization;

namespace FaceFeature
{
    [JsonSerializable(typeof(IAsyncEnumerable<FaceDetection>))]
    [JsonSerializable(typeof(FaceInfo))]
    [JsonSerializable(typeof(FaceInfo[]))]
    [JsonSerializable(typeof(FaceError))]
    [JsonSerializable(typeof(FaceDeleteResponse))]
    [JsonSerializable(typeof(PersistedFace[]))]
    [JsonSerializable(typeof(UrlDetectRequest))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(FacePerson[]))]
    [JsonSerializable(typeof(IAsyncEnumerable<FaceRecognition>))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
