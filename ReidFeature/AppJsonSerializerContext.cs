using ReidFeature.Payloads;
using System.Text.Json.Serialization;

namespace ReidFeature
{
    [JsonSerializable(typeof(List<PersonDetection>))]
    [JsonSerializable(typeof(PersonRecognition))]
    [JsonSerializable(typeof(EnrollResult))]
    [JsonSerializable(typeof(EnrollBatchResult))]
    [JsonSerializable(typeof(EnrollSegmentInfo))]
    [JsonSerializable(typeof(MergeMembersRequest))]
    [JsonSerializable(typeof(MemberInfo[]))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(byte[]))]
    [JsonSerializable(typeof(GalleryData))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
