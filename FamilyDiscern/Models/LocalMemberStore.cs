using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyDiscern.Models;

/// <summary>
/// 本地记录的成员注册信息（服务端响应 + 提交参数）
/// </summary>
public class LocalMemberRecord
{
    public string MemberId { get; set; } = "";
    public string Name { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Mp4Path { get; set; } = "";
    public double FrameIntervalSeconds { get; set; }
    public string RegisterTime { get; set; } = "";
}

/// <summary>
/// 本地成员存储 — 保存所有注册过的成员信息到 JSON 文件
/// </summary>
public class LocalMemberStore
{
    private static readonly string StorePath = Path.Combine(AppContext.BaseDirectory, "members.json");

    public List<LocalMemberRecord> Members { get; set; } = [];

    public static LocalMemberStore Load()
    {
        if (!File.Exists(StorePath))
            return new LocalMemberStore();

        var json = File.ReadAllText(StorePath);
        return JsonSerializer.Deserialize(json, LocalMemberStoreContext.Default.LocalMemberStore) ?? new LocalMemberStore();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, LocalMemberStoreContext.Default.LocalMemberStore);
        File.WriteAllText(StorePath, json);
    }

    public void Add(LocalMemberRecord record)
    {
        Members.Add(record);
        Save();
    }

    public void Remove(string memberId)
    {
        Members.RemoveAll(m => m.MemberId == memberId);
        Save();
    }

    public LocalMemberRecord? Find(string memberId)
    {
        return Members.Find(m => m.MemberId == memberId);
    }
}

[JsonSerializable(typeof(LocalMemberStore))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class LocalMemberStoreContext : JsonSerializerContext;
