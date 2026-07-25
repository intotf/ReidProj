using ReIdSample.Models.Dtos;
using System.Net.Http.Headers;
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
    public async Task<List<ReidPersonDetection>> DetectAsync(Stream imageStream, CancellationToken ct = default)
    {
        using var content = new StreamContent(imageStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PostAsync("/detect/image", content, ct);
        var result = await response.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<ReidDetectResponse>(_jsonOptions, ct);
        _logger.LogInformation("ReidFeature 检测完成: {Count} 个人物", result?.Persons?.Count ?? 0);

        return result?.Persons ?? [];
    }

    /// <summary>
    /// 通过图片 URL 进行检测（POST /detect/url）
    /// </summary>
    public async Task<List<ReidPersonDetection>> DetectByUrlAsync(string imageUrl, CancellationToken ct = default)
    {
        var body = new { imageUrl };
        var response = await _httpClient.PostAsJsonAsync("/detect/url", body, ct);
        var result = await response.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<ReidDetectResponse>(_jsonOptions, ct);
        _logger.LogInformation("ReidFeature URL 检测完成: {Count} 个人物", result?.Persons?.Count ?? 0);

        return result?.Persons ?? [];
    }
}
