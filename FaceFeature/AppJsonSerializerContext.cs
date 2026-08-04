using FaceFeature.Payloads;
using System.Text.Json.Serialization;

namespace FaceFeature
{
    /// <summary>
    /// AOT 友好的 JSON 序列化上下文，集中声明本项目 API 所需的可序列化类型
    /// </summary>
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
