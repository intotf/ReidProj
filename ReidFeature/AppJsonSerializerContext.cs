using ReidFeature.Payloads;
using System.Text.Json.Serialization;

namespace ReidFeature
{
    [JsonSerializable(typeof(List<PersonDetection>))]
    [JsonSerializable(typeof(PersonRecognition))]
    [JsonSerializable(typeof(EnrollResult))]
    [JsonSerializable(typeof(MemberInfo[]))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(byte[]))]
    [JsonSerializable(typeof(GalleryData))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
