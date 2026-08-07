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
    /// 注册家庭成员（流式传输裸流，服务端自动识别编码）
    /// </summary>
    public async Task<EnrollResult?> EnrollAsync(Stream rawStream, string groupId, string memberName, double frameInterval = 0.5, bool append = false, CancellationToken ct = default)
    {
        var endpoint = $"/family/enroll/{Uri.EscapeDataString(groupId)}/{Uri.EscapeDataString(memberName)}?frameIntervalSeconds={frameInterval}&append={append}";

        using var content = new StreamContent(rawStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, ApiJsonContext.Default.EnrollResult);
    }

    /// <summary>
    /// 识别视频中的人物（流式传输裸流，服务端自动识别编码）
    /// </summary>
    public async Task<PersonRecognition?> RecognizeAsync(Stream rawStream, string groupId, double frameInterval = 0.5, float wCloth = 0.30f, float wHead = 0.30f, float wBody = 0.30f, float wGait = 0.10f, float highConfidenceThreshold = 0.965f, CancellationToken ct = default)
    {
        var endpoint = $"/recognize/{Uri.EscapeDataString(groupId)}?frameIntervalSeconds={frameInterval}&wCloth={wCloth}&wHead={wHead}&wBody={wBody}&wGait={wGait}&highConfidenceThreshold={highConfidenceThreshold}";

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
        var response = await _httpClient.GetAsync($"/family/{Uri.EscapeDataString(groupId)}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, ApiJsonContext.Default.ListFamilyMember) ?? [];
    }

    /// <summary>
    /// 删除成员
    /// </summary>
    public async Task<bool> DeleteMemberAsync(string groupId, string memberId, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"/family/{Uri.EscapeDataString(groupId)}/{Uri.EscapeDataString(memberId)}", ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// 同一人多段注册：multipart 一次上传多段裸流文件，各段特征等权融合为一条成员（同名自动合并更新）
    /// </summary>
    /// <param name="rawFilePaths">多段 H264/H265 Annex-B 裸流文件路径</param>
    /// <param name="groupId">分组 ID</param>
    /// <param name="memberName">成员名称</param>
    /// <param name="frameInterval">帧间隔秒数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>批量注册结果</returns>
    public async Task<EnrollBatchResult?> EnrollBatchAsync(
        IReadOnlyList<string> rawFilePaths,
        string groupId,
        string memberName,
        double frameInterval = 0.5,
        bool append = false,
        CancellationToken ct = default)
    {
        var endpoint = $"/family/enroll-batch/{Uri.EscapeDataString(groupId)}/{Uri.EscapeDataString(memberName)}?frameIntervalSeconds={frameInterval}&append={append}";

        using var content = new MultipartFormDataContent();
        foreach (var path in rawFilePaths)
        {
            // MultipartFormDataContent 随自身释放时统一关闭各 StreamContent 的流
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var part = new StreamContent(stream);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, "videos", Path.GetFileName(path));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, ApiJsonContext.Default.EnrollBatchResult);
    }

    /// <summary>
    /// 成员合并去重：把多个成员特征等权融合进目标成员，并删除被合并成员
    /// </summary>
    /// <param name="groupId">分组 ID</param>
    /// <param name="targetMemberId">保留的目标成员 ID</param>
    /// <param name="mergeMemberIds">待合并进目标成员的成员 ID 列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并后的成员列表</returns>
    public async Task<List<FamilyMember>> MergeMembersAsync(
        string groupId,
        string targetMemberId,
        IReadOnlyList<string> mergeMemberIds,
        CancellationToken ct = default)
    {
        var endpoint = $"/family/merge/{Uri.EscapeDataString(groupId)}";
        var requestBody = new MergeMembersRequest
        {
            TargetMemberId = targetMemberId,
            MergeMemberIds = [.. mergeMemberIds]
        };
        var requestJson = JsonSerializer.Serialize(requestBody, ApiJsonContext.Default.MergeMembersRequest);

        using var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(endpoint, content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, ApiJsonContext.Default.ListFamilyMember) ?? [];
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
