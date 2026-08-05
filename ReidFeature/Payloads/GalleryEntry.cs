namespace ReidFeature.Payloads;

/// <summary>
/// Gallery 成员持久化条目
/// </summary>
internal sealed class GalleryEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime EnrolledAt { get; set; }
    public TrackFeaturePack FeaturePack { get; set; } = new();
}
