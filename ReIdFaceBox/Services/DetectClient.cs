using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ReIdFaceBox.Models;

namespace ReIdFaceBox.Services;

public class DetectClient : IDisposable
{
    private HttpClient _httpClient;

    public DetectClient(string baseUrl)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public void UpdateBaseUrl(string baseUrl)
    {
        _httpClient.Dispose();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public async Task<List<PersonDetection>> DetectAsync(byte[] imageBytes, int flags = 0, CancellationToken ct = default)
    {
        using var content = new ByteArrayContent(imageBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PostAsync($"/detect/image?flags={flags}", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, DetectJsonContext.Default.ListPersonDetection) ?? [];
    }

    /// <summary>
    /// 发送图片到 /recognize/image/{groupId} 进行目标对比
    /// </summary>
    public async Task<List<PersonRecognition>> RecognizeAsync(byte[] imageBytes, string groupId, float threshold, int flags = 0, CancellationToken ct = default)
    {
        using var content = new ByteArrayContent(imageBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PostAsync(
            $"/recognize/image/{groupId}?similarityThreshold={threshold}&flags={flags}", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, DetectJsonContext.Default.ListPersonRecognition) ?? [];
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
