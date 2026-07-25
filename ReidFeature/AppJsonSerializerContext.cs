using System.Text.Json.Serialization;
using ReidFeature.Payloads;

namespace ReidFeature
{
    [JsonSerializable(typeof(DetectResponse))]
    [JsonSerializable(typeof(PersonDetection[]))]
    [JsonSerializable(typeof(BoundingBox))]
    [JsonSerializable(typeof(FaceDetection))]
    [JsonSerializable(typeof(DetectionFlags))]
    [JsonSerializable(typeof(Nullable<DetectionFlags>))]
    [JsonSerializable(typeof(UrlDetectRequest))]
    [JsonSerializable(typeof(ErrorResponse))]
    [JsonSerializable(typeof(VideoCodec))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
