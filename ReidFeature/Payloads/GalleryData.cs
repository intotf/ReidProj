namespace ReidFeature.Payloads;

/// <summary>
/// Gallery 持久化数据模型 — 单个分组下的成员列表
/// </summary>
internal sealed class GalleryData
{
    public List<GalleryEntry> Members { get; set; } = [];
}
