using ReIdSample.Models.Dtos;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ReIdSample.Services;

public class ReidFeatureClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ReidFeatureClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ReidFeatureClient(HttpClient httpClient, ILogger<ReidFeatureClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// 上传图片进行检测（POST /detect/image）
    /// </summary>
    public async Task<List<ReidPersonDetection>> HandleImageAsync(Stream imageStream, DetectionFlags? flags = null, CancellationToken ct = default)
    {
        using var content = new StreamContent(imageStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var url = BuildUrl("/detect/image", flags);
        var response = await _httpClient.PostAsync(url, content, ct);
        var result = await response.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<List<ReidPersonDetection>>(_jsonOptions, ct);
        _logger.LogInformation("ReidFeature 检测完成: {Count} 个人物", result?.Count ?? 0);

        return result ?? [];
    }

    /// <summary>
    /// 通过图片 URL 进行检测（POST /detect/imageurl）
    /// </summary>
    public async Task<List<ReidPersonDetection>> HandleImageUrlAsync(string imageUrl, DetectionFlags? flags = null, CancellationToken ct = default)
    {
        var body = new { imageUrl };
        var url = BuildUrl("/detect/imageurl", flags);
        var response = await _httpClient.PostAsJsonAsync(url, body, ct);
        var result = await response.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<List<ReidPersonDetection>>(_jsonOptions, ct);
        _logger.LogInformation("ReidFeature URL 检测完成: {Count} 个人物", result?.Count ?? 0);

        return result ?? [];
    }

    /// <summary>
    /// 上传 H264 裸流进行检测（POST /detect/h264stream）
    /// </summary>
    /// <param name="h264Stream">H264 裸流数据流</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧），如 5 表示每 5 秒一帧</param>
    /// <param name="flags">检测功能标志位</param>
    /// <param name="ct">取消令牌</param>
    public async IAsyncEnumerable<ReidPersonDetection> HandleH264Async(Stream h264Stream, int frameIntervalSeconds, DetectionFlags? flags = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var content = new StreamContent(h264Stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var url = BuildUrl("/detect/h264stream", flags, frameIntervalSeconds);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        int count = 0;
        await foreach (var item in response.Content.ReadFromJsonAsAsyncEnumerable<ReidPersonDetection>(_jsonOptions, ct))
        {
            if (item is not null)
            {
                count++;
                yield return item;
            }
        }

        _logger.LogInformation("ReidFeature H264 检测完成: {Count} 个人物", count);
    }

    /// <summary>
    /// 上传 H265 裸流进行检测（POST /detect/h265stream）
    /// </summary>
    /// <param name="h265Stream">H265 裸流数据流</param>
    /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）</param>
    /// <param name="flags">检测功能标志位</param>
    /// <param name="ct">取消令牌</param>
    public async IAsyncEnumerable<ReidPersonDetection> HandleH265Async(Stream h265Stream, int frameIntervalSeconds, DetectionFlags? flags = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var content = new StreamContent(h265Stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var url = BuildUrl("/detect/h265stream", flags, frameIntervalSeconds);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        int count = 0;
        await foreach (var item in response.Content.ReadFromJsonAsAsyncEnumerable<ReidPersonDetection>(_jsonOptions, ct))
        {
            if (item is not null)
            {
                count++;
                yield return item;
            }
        }

        _logger.LogInformation("ReidFeature H265 检测完成: {Count} 个人物", count);
    }

    private static string BuildUrl(string basePath, DetectionFlags? flags, int? frameIntervalSeconds = null)
    {
        var query = new List<string>();
        if (flags.HasValue)
            query.Add($"flags={(int)flags.Value}");
        if (frameIntervalSeconds.HasValue)
            query.Add($"frameIntervalSeconds={frameIntervalSeconds.Value}");

        return query.Count > 0 ? $"{basePath}?{string.Join("&", query)}" : basePath;
    }
}
