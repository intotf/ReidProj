using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// 本地成员存储。远端已注册成员是权威数据源，本文件额外保存视频路径等本地元数据。
/// </summary>
public class LocalMemberStore
{
    private static readonly string StorePath = Path.Combine(AppContext.BaseDirectory, "members.json");
    private static readonly object SyncRoot = new();

    public List<LocalMemberRecord> Members { get; set; } = [];

    public static LocalMemberStore Load()
    {
        lock (SyncRoot)
        {
            return LoadCore();
        }
    }

    /// <summary>
    /// 新增或更新注册记录，避免同一组、同一成员 ID 重复写入。
    /// </summary>
    public static void Upsert(LocalMemberRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.GroupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.MemberId);

        lock (SyncRoot)
        {
            var store = LoadCore();
            store.Members.RemoveAll(m => IsSameMember(m, record.GroupId, record.MemberId));
            store.Members.Add(record);
            SaveCore(store);
        }
    }

    /// <summary>
    /// 删除指定组的本地成员记录。
    /// </summary>
    public static bool Remove(string groupId, string memberId)
    {
        lock (SyncRoot)
        {
            var store = LoadCore();
            var removed = store.Members.RemoveAll(m => IsSameMember(m, groupId, memberId)) > 0;
            if (removed)
            {
                SaveCore(store);
            }
            return removed;
        }
    }

    /// <summary>
    /// 将成功查询到的组与远端注册成员同步。同步组中的陈旧记录会被删除；
    /// 未查询或查询失败的组保持不变。匹配成员的视频路径等本地元数据会保留。
    /// </summary>
    public static LocalMemberStore Synchronize(
        IReadOnlyDictionary<string, IReadOnlyCollection<FamilyMember>> registeredByGroup)
    {
        lock (SyncRoot)
        {
            var store = LoadCore();
            if (registeredByGroup.Count == 0)
            {
                return store;
            }

            foreach (var (groupId, registeredMembers) in registeredByGroup)
            {
                if (string.IsNullOrWhiteSpace(groupId))
                {
                    continue;
                }

                var existingById = store.Members
                    .Where(m => string.Equals(m.GroupId, groupId, StringComparison.Ordinal) &&
                                !string.IsNullOrWhiteSpace(m.MemberId))
                    .GroupBy(m => m.MemberId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

                var remoteById = registeredMembers
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                    .GroupBy(m => m.Id, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

                // 远端是该组的权威数据：先移除整个组，再按远端结果重建。
                store.Members.RemoveAll(m =>
                    string.Equals(m.GroupId, groupId, StringComparison.Ordinal));

                foreach (var (memberId, remote) in remoteById)
                {
                    existingById.TryGetValue(memberId, out var local);
                    store.Members.Add(new LocalMemberRecord
                    {
                        MemberId = memberId,
                        Name = remote.Name,
                        GroupId = groupId,
                        Mp4Path = local?.Mp4Path ?? "",
                        FrameIntervalSeconds = local?.FrameIntervalSeconds ?? 0,
                        RegisterTime = !string.IsNullOrWhiteSpace(local?.RegisterTime)
                            ? local.RegisterTime
                            : FormatRegisterTime(remote.EnrolledAt),
                    });
                }
            }

            SaveCore(store);
            return store;
        }
    }

    public static LocalMemberStore SynchronizeGroup(
        string groupId,
        IReadOnlyCollection<FamilyMember> registeredMembers) =>
        Synchronize(new Dictionary<string, IReadOnlyCollection<FamilyMember>>(StringComparer.Ordinal)
        {
            [groupId] = registeredMembers,
        });

    public LocalMemberRecord? Find(string groupId, string memberId) =>
        Members.Find(m => IsSameMember(m, groupId, memberId));

    private static LocalMemberStore LoadCore()
    {
        if (!File.Exists(StorePath))
        {
            return new LocalMemberStore();
        }

        var json = File.ReadAllText(StorePath);
        return JsonSerializer.Deserialize(json, LocalMemberStoreContext.Default.LocalMemberStore)
            ?? new LocalMemberStore();
    }

    private static void SaveCore(LocalMemberStore store)
    {
        var json = JsonSerializer.Serialize(store, LocalMemberStoreContext.Default.LocalMemberStore);
        var tempPath = $"{StorePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, StorePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static bool IsSameMember(LocalMemberRecord record, string groupId, string memberId) =>
        string.Equals(record.GroupId, groupId, StringComparison.Ordinal) &&
        string.Equals(record.MemberId, memberId, StringComparison.Ordinal);

    private static string FormatRegisterTime(DateTime enrolledAt) =>
        enrolledAt == default
            ? ""
            : enrolledAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

[JsonSerializable(typeof(LocalMemberStore))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class LocalMemberStoreContext : JsonSerializerContext;
