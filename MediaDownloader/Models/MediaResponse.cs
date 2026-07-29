using System.Text.Json.Serialization;

namespace MediaDownloader.Models;

/// <summary>
/// 媒体列表 API 响应
/// </summary>
public class MediaListResponse
{
    [JsonPropertyName("data")]
    public MediaData Data { get; set; } = new();
}

public class MediaData
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("items")]
    public List<MediaItem> Items { get; set; } = [];
}

public class MediaItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("fileKey")]
    public string FileKey { get; set; } = "";

    [JsonPropertyName("mediaType")]
    public int MediaType { get; set; }

    [JsonPropertyName("mediaTime")]
    public string MediaTime { get; set; } = "";

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = "";

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = "";

    [JsonPropertyName("deviceNum")]
    public string DeviceNum { get; set; } = "";
}
