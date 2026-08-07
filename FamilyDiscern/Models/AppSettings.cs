using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyDiscern.Models;

public class AppSettings
{
    public string ServerUrl { get; set; } = "http://localhost:9000";
    public string FfmpegPath { get; set; } = @"G:\Tools\ffmpeg\ffmpeg.exe";
    public double FrameIntervalSeconds { get; set; } = 0.5;
    public float WCloth { get; set; } = 0.30f;
    public float WHead { get; set; } = 0.30f;
    public float WBody { get; set; } = 0.30f;
    public float WGait { get; set; } = 0.10f;
    public float HighConfidenceThreshold { get; set; } = 0.965f;
    public List<string> HistoryGroups { get; set; } = [];

    private static readonly string DefaultPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppSettings Load(string? path = null)
    {
        var filePath = path ?? DefaultPath;
        if (!File.Exists(filePath))
            return new AppSettings();
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings) ?? new AppSettings();
    }

    public void Save(string? path = null)
    {
        var filePath = path ?? DefaultPath;
        var json = JsonSerializer.Serialize(this, AppSettingsContext.Default.AppSettings);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 添加历史组名（去重）
    /// </summary>
    public void AddGroup(string groupId)
    {
        if (!string.IsNullOrWhiteSpace(groupId) && !HistoryGroups.Contains(groupId))
        {
            HistoryGroups.Add(groupId);
        }
    }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppSettingsContext : JsonSerializerContext;
