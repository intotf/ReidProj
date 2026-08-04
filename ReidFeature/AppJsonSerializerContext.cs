using ReidFeature.Payloads;
using ReidFeature.Services;
using System.Text.Json.Serialization;

namespace ReidFeature
{
    [JsonSerializable(typeof(List<PersonDetection>))]
    [JsonSerializable(typeof(PersonRecognition))]
    [JsonSerializable(typeof(EnrollResult))]
    [JsonSerializable(typeof(MemberInfo[]))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(FamilyGalleryService.GalleryData))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
