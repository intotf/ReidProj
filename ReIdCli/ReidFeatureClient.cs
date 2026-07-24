using System.Net.Http.Headers;
using System.Text.Json;

namespace ReIdCli;

/// <summary>
/// 调用 Re-ID 推理服务的 HTTP 客户端
/// </summary>
public class ReidFeatureClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public ReidFeatureClient(string baseUrl)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// 发送图片到 /detect 接口，返回检测到的人物及其特征向量
    /// </summary>
    public async Task<List<PersonDetection>> DetectAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        using var content = new ByteArrayContent(imageBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PostAsync("/detect", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.DetectResponse);

        return result?.Persons ?? [];
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
