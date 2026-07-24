using System.Net.Http.Headers;
using System.Text.Json;
using ReIdSample.Models.Dtos;

namespace ReIdSample.Services;

public class ReidFeatureClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ReidFeatureClient> _logger;

    public ReidFeatureClient(HttpClient httpClient, ILogger<ReidFeatureClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<ReidPersonDetection>> DetectAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        using var content = new ByteArrayContent(imageBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PostAsync("/detect", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("ReidFeature 响应: {Len} bytes", json.Length);

        var result = JsonSerializer.Deserialize<ReidDetectResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result?.Persons ?? [];
    }
}
