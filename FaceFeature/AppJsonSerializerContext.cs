using FaceFeature.Payloads;
using System.Text.Json.Serialization;

namespace FaceFeature
{
    [JsonSerializable(typeof(FaceDetection))]
    [JsonSerializable(typeof(FaceInfo))]
    [JsonSerializable(typeof(FaceInfo[]))]
    [JsonSerializable(typeof(FaceError))]
    [JsonSerializable(typeof(FaceDeleteResponse))]
    [JsonSerializable(typeof(PersistedFace[]))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(FaceRecognition))]
    [JsonSerializable(typeof(byte[]))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(bool))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
