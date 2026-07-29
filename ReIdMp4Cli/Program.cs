using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReIdMp4Cli;

Console.OutputEncoding = Encoding.UTF8;

// ── 加载配置文件 ─────────────────────────────────
var config = LoadConfig();
if (config == null)
{
    Console.Error.WriteLine("错误: 无法加载 appsettings.json 配置文件");
    return 1;
}

// ── 解析命令行参数 ─────────────────────────────────
if (args.Length < 1)
{
    Console.WriteLine("用法: ReIdMp4Cli <mp4文件路径> [分组ID] [选项]");
    Console.WriteLine();
    Console.WriteLine("参数:");
    Console.WriteLine("  <mp4文件路径>           待识别的 MP4 视频绝对路径");
    Console.WriteLine("  [分组ID]                人物分组标识 (默认: {0})", config.DefaultGroupId);
    Console.WriteLine();
    Console.WriteLine("选项:");
    Console.WriteLine("  --server-url <url>       ReidFeature 服务地址 (默认: {0})", config.ServerUrl);
    Console.WriteLine("  --threshold <float>      相似度阈值 (默认: {0})", config.Threshold);
    Console.WriteLine("  --flags <int>            检测标志位: 0=All, 1=SkipFaceDetection, 2=StopOnFirstFrameHit (默认: {0})", config.Flags);
    Console.WriteLine("  --ffmpeg-path <path>     ffmpeg 可执行文件路径 (默认: {0} 或 PATH)", string.IsNullOrEmpty(config.FfmpegPath) ? "自动查找" : config.FfmpegPath);
    Console.WriteLine("  --save-matched           匹配时保存抽帧图片到 {mp4文件名}_temp 目录");
    Console.WriteLine();
    Console.WriteLine("示例:");
    Console.WriteLine("  ReIdMp4Cli \"D:\\Videos\\test.mp4\"");
    Console.WriteLine("  ReIdMp4Cli \"D:\\Videos\\test.mp4\" family01 --server-url http://192.168.1.100:5000");
    return 1;
}

var mp4Path = args[0];
var groupId = args.Length >= 2 && !args[1].StartsWith("--") ? args[1] : config.DefaultGroupId;

// 解析命名参数（groupId 占 args[1] 时从 2 开始，否则从 1 开始）
var namedStartIndex = args.Length >= 2 && !args[1].StartsWith("--") ? 2 : 1;
var serverUrl = config.ServerUrl;
var threshold = config.Threshold;
var flags = config.Flags;
var ffmpegPath = !string.IsNullOrEmpty(config.FfmpegPath) ? config.FfmpegPath : FindFfmpeg();
var ffmpegArgsTemplate = config.FfmpegArgs;
var saveMatched = false;

for (int i = namedStartIndex; i < args.Length; i++)
{
    if (args[i] == "--server-url" && i + 1 < args.Length)
        serverUrl = args[++i];
    else if (args[i] == "--threshold" && i + 1 < args.Length)
        threshold = float.TryParse(args[++i], out var t) ? t : threshold;
    else if (args[i] == "--flags" && i + 1 < args.Length)
        flags = int.TryParse(args[++i], out var f) ? f : flags;
    else if (args[i] == "--ffmpeg-path" && i + 1 < args.Length)
        ffmpegPath = args[++i];
    else if (args[i] == "--save-matched")
        saveMatched = true;
}

// ── 校验参数 ──────────────────────────────────────
if (!File.Exists(mp4Path))
{
    Console.Error.WriteLine($"错误: 文件不存在 - {mp4Path}");
    return 1;
}

if (string.IsNullOrEmpty(ffmpegPath))
{
    Console.Error.WriteLine("错误: 未找到 ffmpeg，请通过 --ffmpeg-path 指定路径");
    return 1;
}

if (!File.Exists(ffmpegPath))
{
    Console.Error.WriteLine($"错误: ffmpeg 不存在 - {ffmpegPath}");
    return 1;
}

Console.WriteLine($"=== ReIdMp4Cli ===");
Console.WriteLine($"视频文件:   {mp4Path}");
Console.WriteLine($"分组 ID:    {groupId}");
Console.WriteLine($"服务地址:   {serverUrl}");
Console.WriteLine($"相似度阈值: {threshold}");
Console.WriteLine($"检测标志:   {flags}");
Console.WriteLine($"ffmpeg:     {ffmpegPath}");
Console.WriteLine();

// ── 第 1 步：创建临时目录，用 ffmpeg 抽帧 ──────────
var tempDir = Path.Combine(Path.GetTempPath(), "reidmp4cli_" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(tempDir);

try
{
    Console.WriteLine("[1/3] 抽帧...");
    var frameCount = await ExtractFramesAsync(ffmpegPath, mp4Path, tempDir, ffmpegArgsTemplate);
    if (frameCount == 0)
    {
        Console.Error.WriteLine("错误: 未抽到任何帧");
        return 1;
    }
    Console.WriteLine($"  抽取 {frameCount} 帧 (1 fps)");
    Console.WriteLine();

    // ── 第 2 步：逐帧发送识别请求 ────────────────────
    Console.WriteLine("[2/3] 逐帧识别...");
    var frameFiles = Directory.GetFiles(tempDir, "*.jpg")
        .OrderBy(f => f)
        .ToArray();

    var httpClient = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromSeconds(60) };
    int matchCount = 0;
    var allMatches = new List<(string FrameName, PersonRecognition Rec)>();

    // 匹配帧保存目录
    string? matchedDir = null;
    if (saveMatched)
    {
        var mp4Name = Path.GetFileNameWithoutExtension(mp4Path);
        matchedDir = Path.Combine(Path.GetDirectoryName(mp4Path) ?? ".", $"{mp4Name}_temp");
        Directory.CreateDirectory(matchedDir);
        Console.WriteLine($"  匹配帧保存: {matchedDir}");
    }

    foreach (var framePath in frameFiles)
    {
        var frameName = Path.GetFileName(framePath);
        Console.Write($"  帧 {frameName}...");

        try
        {
            var frameBytes = await File.ReadAllBytesAsync(framePath);
            var recognitions = await RecognizeImageAsync(httpClient, groupId, frameBytes, threshold, flags);

            if (recognitions.Count > 0)
            {
                matchCount++;
                Console.WriteLine($" ✓ 匹配到 {recognitions.Count} 个!");

                // 保存匹配帧到 {mp4文件名}_temp 目录
                if (matchedDir != null)
                {
                    var savePath = Path.Combine(matchedDir, frameName);
                    File.Copy(framePath, savePath, overwrite: true);
                }

                foreach (var rec in recognitions)
                {
                    allMatches.Add((frameName, rec));
                    Console.WriteLine($"     人物: {rec.Name} (ID: {rec.Id}) | " +
                                      $"相似度: {rec.ReidSimilarity:F4} | " +
                                      $"来源图片: {rec.SourceFile ?? "未知"}");
                }
            }
            else
            {
                Console.WriteLine(" × 无匹配");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($" ! 请求失败: {ex.Message}");
        }
    }

    // ── 第 3 步：输出汇总 ──────────────────────────
    Console.WriteLine();
    Console.WriteLine("[3/3] 汇总:");
    Console.WriteLine(new string('─', 60));
    Console.WriteLine($"总帧数:      {frameCount}");
    Console.WriteLine($"匹配帧数:    {matchCount}");
    Console.WriteLine($"匹配率:      {(frameCount > 0 ? matchCount * 100f / frameCount : 0):F1}%");

    if (allMatches.Count > 0)
    {
        var best = allMatches.OrderByDescending(m => m.Rec.ReidSimilarity).First();
        Console.WriteLine();
        Console.WriteLine("🏆 最高相似度匹配:");
        Console.WriteLine($"     帧:      {best.FrameName}");
        Console.WriteLine($"     人物:    {best.Rec.Name} (ID: {best.Rec.Id})");
        Console.WriteLine($"     相似度:  {best.Rec.ReidSimilarity:F4}");
        Console.WriteLine($"     目标图片: {best.Rec.SourceFile ?? "未知"}");
    }

    if (matchedDir != null)
        Console.WriteLine($"匹配帧保存:   {matchedDir}");

    Console.WriteLine(new string('─', 60));

    return matchCount > 0 ? 0 : 1;
}
finally
{
    // 清理临时文件
    try { Directory.Delete(tempDir, true); } catch { }
}

// ═══════════════════════════════════════════════
//  本地函数
// ═══════════════════════════════════════════════

/// <summary>
/// 使用 ffmpeg 抽取帧到临时目录
/// </summary>
static async Task<int> ExtractFramesAsync(string ffmpegPath, string inputVideo, string outputDir, string argsTemplate)
{
    var outputPattern = Path.Combine(outputDir, "frame_%04d.jpg");
    var arguments = argsTemplate
        .Replace("{inputVideo}", inputVideo)
        .Replace("{outputPattern}", outputPattern);
    var psi = new ProcessStartInfo(ffmpegPath)
    {
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = Process.Start(psi)!;
    // ffmpeg 输出在 stderr 上
    _ = Task.Run(() => process.StandardError.ReadToEndAsync());
    await process.WaitForExitAsync();

    if (process.ExitCode != 0) return 0;

    return Directory.GetFiles(outputDir, "*.jpg").Length;
}

/// <summary>
/// 发送帧到 /recognize/image/{groupId} 并解析流式识别结果
/// </summary>
static async Task<List<PersonRecognition>> RecognizeImageAsync(
    HttpClient client, string groupId, byte[] imageBytes, float threshold, int flags)
{
    using var content = new ByteArrayContent(imageBytes);
    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

    var response = await client.PostAsync($"/recognize/image/{groupId}?similarityThreshold={threshold}&flags={flags}", content);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListPersonRecognition) ?? [];
}

/// <summary>
/// 从 PATH 环境变量查找 ffmpeg
/// </summary>
static string? FindFfmpeg()
{
    // 常见安装位置
    var commonPaths = new[]
    {
        "ffmpeg.exe",
        @"G:\Tools\ffmpeg\ffmpeg.exe"
    };

    foreach (var path in commonPaths)
    {
        if (File.Exists(path)) return Path.GetFullPath(path);
    }

    // 从 PATH 查找
    var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
        ?? [];
    foreach (var dir in pathDirs)
    {
        var full = Path.Combine(dir.Trim(), "ffmpeg.exe");
        if (File.Exists(full)) return full;
    }

    return null;
}

/// <summary>
/// 加载 appsettings.json 配置文件
/// </summary>
static AppConfig? LoadConfig()
{
    var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(configPath))
    {
        configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
    }
    if (!File.Exists(configPath))
    {
        return null;
    }
    try
    {
        var json = File.ReadAllText(configPath);
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("AppConfig", out var section))
        {
            return JsonSerializer.Deserialize(section, AppJsonContext.Default.AppConfig);
        }
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig);
    }
    catch
    {
        return null;
    }
}

/// <summary>
/// /recognize/image/{groupId} 返回的识别结果 DTO
/// </summary>
public class PersonRecognition
{
    public string Id { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Name { get; set; } = "";
    public float ReidSimilarity { get; set; }
    public string? SourceFile { get; set; }
}

/// <summary>
/// AOT 兼容 JSON 序列化上下文
/// </summary>
[JsonSerializable(typeof(PersonRecognition))]
[JsonSerializable(typeof(List<PersonRecognition>))]
[JsonSerializable(typeof(AppConfig))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class AppJsonContext : JsonSerializerContext;
