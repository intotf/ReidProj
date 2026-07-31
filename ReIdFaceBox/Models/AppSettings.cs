using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReIdFaceBox.Models;

public class AppSettings
{
    public string ReidServiceUrl { get; set; } = "http://localhost:9000";
    public string FfmpegPath { get; set; } = @"G:\Tools\ffmpeg\ffmpeg.exe";
    public string FfmpegArgs { get; set; } = "-i \"{input}\" -vf fps=1 -q:v 2 -f image2pipe -vcodec mjpeg pipe:1";
    public int DetectionFlags { get; set; } = 0;
    public string GroupId { get; set; } = "group2";
    public float SimilarityThreshold { get; set; } = 0.5f;

    // 提取目标数据设置
    public bool ExtractCheckFace { get; set; } = true;
    public int ExtractFaceMinWidth { get; set; } = 0;
    public int ExtractFaceMinHeight { get; set; } = 0;
    public string ExtractConditionLogic { get; set; } = "And";
    public bool ExtractCheckBody { get; set; } = true;
    public int ExtractBodyMinWidth { get; set; } = 0;
    public int ExtractBodyMinHeight { get; set; } = 0;
    public bool ExtractCropBbox { get; set; } = false;
    public string ExtractOutputDir { get; set; } = "";

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
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppSettingsContext : JsonSerializerContext;
