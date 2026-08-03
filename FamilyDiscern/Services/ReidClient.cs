using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FamilyDiscern.Models;

namespace FamilyDiscern.Services;

/// <summary>
/// ReidFeature 服务 HTTP 客户端
/// </summary>
public class ReidClient : IDisposable
{
    private HttpClient _httpClient;

    public ReidClient(string baseUrl)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    public void UpdateBaseUrl(string baseUrl)
    {
        _httpClient.Dispose();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    /// <summary>
    /// 注册家庭成员（流式传输裸流）
    /// </summary>
    public async Task<EnrollResult?> EnrollAsync(Stream rawStream, VideoCodec codec, string groupId, string memberName, double frameInterval = 0.5, CancellationToken ct = default)
    {
        var endpoint = codec == VideoCodec.H265
            ? $"/family/enroll/h265/{groupId}/{memberName}?frameIntervalSeconds={frameInterval}"
            : $"/family/enroll/h264/{groupId}/{memberName}?frameIntervalSeconds={frameInterval}";

        using var content = new StreamContent(rawStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, ApiJsonContext.Default.EnrollResult);
    }

    /// <summary>
    /// 识别视频中的人物（流式传输裸流）
    /// </summary>
    public async Task<PersonRecognition?> RecognizeAsync(Stream rawStream, VideoCodec codec, string groupId, double frameInterval = 0.5, float wCloth = 0.20f, float wHead = 0.30f, float wBody = 0.30f, float wGait = 0.20f, CancellationToken ct = default)
    {
        var endpoint = codec == VideoCodec.H265
            ? $"/recognize/h265stream/{groupId}?frameIntervalSeconds={frameInterval}&wCloth={wCloth}&wHead={wHead}&wBody={wBody}&wGait={wGait}"
            : $"/recognize/h264stream/{groupId}?frameIntervalSeconds={frameInterval}&wCloth={wCloth}&wHead={wHead}&wBody={wBody}&wGait={wGait}";

        using var content = new StreamContent(rawStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, ApiJsonContext.Default.PersonRecognition);
    }

    /// <summary>
    /// 列出组内成员
    /// </summary>
    public async Task<List<FamilyMember>> ListMembersAsync(string groupId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/family/{groupId}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, ApiJsonContext.Default.ListFamilyMember) ?? [];
    }

    /// <summary>
    /// 删除成员
    /// </summary>
    public async Task<bool> DeleteMemberAsync(string groupId, string memberId, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"/family/{groupId}/{memberId}", ct);
        return response.IsSuccessStatusCode;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
