using System.Net.Http.Headers;
using System.Text.Json;
using ReIdSample.Models.Dtos;

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

    public async Task<List<ReidPersonDetection>> DetectAsync(Stream imageStream, CancellationToken ct = default)
    {
        using var content = new StreamContent(imageStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PostAsync("/detect", content, ct);
        var result = await response.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<ReidDetectResponse>(_jsonOptions, ct);
        _logger.LogInformation("ReidFeature 检测完成: {Count} 个人物", result?.Persons?.Count ?? 0);

        return result?.Persons ?? [];
    }
}
