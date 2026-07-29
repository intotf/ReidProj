using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using MediaDownloader;
using MediaDownloader.Models;

Console.OutputEncoding = Encoding.UTF8;

// ── 加载配置 ─────────────────────────────────────
#if DEBUG
var envName = "Development";
#else
var envName = "";
#endif

var config = LoadConfig(envName);
if (config == null)
{
    Console.Error.WriteLine("错误: 无法加载 appsettings.json 配置文件");
    return 1;
}

// ── 解析命令行参数 ─────────────────────────────────
var deviceIds = new List<string>();
var outputDir = config.DownloadDir;
var pageSize = config.PageSize;
var maxFiles = config.MaxFiles;
var apiBaseUrl = config.ApiBaseUrl;
var authToken = config.AuthToken;
var awsAccessKey = config.Aws.AccessKey;
var awsSecretKey = config.Aws.SecretKey;
var awsRegion = config.Aws.Region;
var awsBucket = config.Aws.BucketName;
var minCreationTime = config.MinCreationTime;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--device-id" && i + 1 < args.Length)
        deviceIds.Add(args[++i]);
    else if (args[i] == "--output-dir" && i + 1 < args.Length)
        outputDir = args[++i];
    else if (args[i] == "--page-size" && i + 1 < args.Length)
        pageSize = int.TryParse(args[++i], out var ps) ? ps : pageSize;
    else if (args[i] == "--max-files" && i + 1 < args.Length)
        maxFiles = int.TryParse(args[++i], out var mf) ? mf : maxFiles;
    else if (args[i] == "--token" && i + 1 < args.Length)
        authToken = args[++i];
    else if (args[i] == "--server-url" && i + 1 < args.Length)
        apiBaseUrl = args[++i];
}

// 合并命令行与配置中的设备 ID
if (deviceIds.Count == 0)
    deviceIds = config.DefaultDeviceIds.Count > 0 ? config.DefaultDeviceIds : deviceIds;

if (deviceIds.Count == 0)
{
    Console.Error.WriteLine("错误: 未指定设备 ID，请通过 --device-id 参数或配置文件 DefaultDeviceIds 设置");
    return 1;
}

if (string.IsNullOrEmpty(authToken))
{
    Console.Error.WriteLine("错误: 未指定 AuthToken，请通过 --token 参数或配置文件设置");
    return 1;
}

if (string.IsNullOrEmpty(awsBucket))
{
    Console.Error.WriteLine("错误: 未指定 AWS BucketName，请在配置文件中设置");
    return 1;
}

Console.WriteLine($"=== MediaDownloader ===");
Console.WriteLine($"API:      {apiBaseUrl}");
Console.WriteLine($"设备数:   {deviceIds.Count}");
Console.WriteLine($"保存目录: {outputDir}");
Console.WriteLine($"页大小:   {pageSize}");
Console.WriteLine($"最大文件: {(maxFiles > 0 ? maxFiles.ToString() : "全部")}");
if (!string.IsNullOrEmpty(config.MinCreationTime))
    Console.WriteLine($"最小时间: {config.MinCreationTime}");
Console.WriteLine();

// ── 初始化 AWS S3 客户端 ─────────────────────────
var s3Config = new AmazonS3Config
{
    RegionEndpoint = RegionEndpoint.GetBySystemName(awsRegion),
    Timeout = TimeSpan.FromSeconds(30)
};

AWSCredentials? credentials = null;
if (!string.IsNullOrEmpty(awsAccessKey) && !string.IsNullOrEmpty(awsSecretKey))
    credentials = new BasicAWSCredentials(awsAccessKey, awsSecretKey);

using var s3Client = credentials != null
    ? new AmazonS3Client(credentials, s3Config)
    : new AmazonS3Client(s3Config);

using var httpClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl.TrimEnd('/')), Timeout = TimeSpan.FromSeconds(30) };
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

int totalDownloaded = 0;

// ── 逐设备处理 ────────────────────────────────────
foreach (var deviceId in deviceIds)
{
    Console.WriteLine($"────────────────────────────────────────");
    Console.WriteLine($"设备: {deviceId}");

    // 每个设备独立文件夹
    var deviceDir = Path.Combine(outputDir, deviceId);
    Directory.CreateDirectory(deviceDir);

    // [1/3] 获取媒体列表
    Console.WriteLine("  [1/3] 获取媒体列表...");
    var items = await FetchMediaListAsync(httpClient, deviceId, pageSize, maxFiles, minCreationTime);
    if (items.Count == 0)
    {
        Console.WriteLine("  无媒体文件");
        continue;
    }
    Console.WriteLine($"  共 {items.Count} 个文件");

    // [2/3] 下载文件
    Console.WriteLine("  [2/3] 下载文件...");
    int deviceDownloaded = 0;
    foreach (var item in items)
    {
        var fileName = Path.GetFileName(item.FileKey);
        if (string.IsNullOrEmpty(fileName))
            fileName = $"{item.Id}.mp4";

        var localPath = Path.Combine(deviceDir, fileName);
        Console.Write($"    {fileName} ({FormatSize(item.FileSize)})...");

        try
        {
            var getRequest = new GetObjectRequest
            {
                BucketName = awsBucket,
                Key = item.FileKey
            };

            using var response = await s3Client.GetObjectAsync(getRequest);
            using var fileStream = File.Create(localPath);
            await response.ResponseStream.CopyToAsync(fileStream);

            deviceDownloaded++;
            Console.WriteLine(" ✓");
        }
        catch (Exception ex)
        {
            Console.WriteLine($" ✗ {ex.Message}");
        }
    }

    Console.WriteLine($"  设备 {deviceId}: 成功下载 {deviceDownloaded}/{items.Count} 个");
    totalDownloaded += deviceDownloaded;
}

// ── [3/3] 汇总 ────────────────────────────────────
Console.WriteLine();
Console.WriteLine("[3/3] 汇总:");
Console.WriteLine(new string('─', 50));
Console.WriteLine($"总下载数: {totalDownloaded}");
Console.WriteLine($"保存目录: {Path.GetFullPath(outputDir)}");
Console.WriteLine(new string('─', 50));

return totalDownloaded > 0 ? 0 : 1;

// ═══════════════════════════════════════════════
//  本地函数
// ═══════════════════════════════════════════════

/// <summary>
/// 分页获取设备媒体列表
/// </summary>
static async Task<List<MediaItem>> FetchMediaListAsync(
    HttpClient client, string deviceId, int pageSize, int maxFiles, string? minCreationTime)
{
    var allItems = new List<MediaItem>();
    int page = 1;

    while (true)
    {
        var url = $"/api/admin/media?deviceId={deviceId}&page={page}&pageSize={pageSize}&orderBy=&timeZone=Asia%2FShanghai";
        if (!string.IsNullOrEmpty(minCreationTime))
            url += $"&minCreationTime={Uri.EscapeDataString(minCreationTime)}";

        try
        {
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MediaListResponse>(json);

            if (result?.Data?.Items == null || result.Data.Items.Count == 0)
                break;

            allItems.AddRange(result.Data.Items);

            if (maxFiles > 0 && allItems.Count >= maxFiles)
            {
                allItems = allItems.Take(maxFiles).ToList();
                break;
            }

            if (allItems.Count >= result.Data.TotalCount)
                break;

            page++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    获取列表失败 (第 {page} 页): {ex.Message}");
            break;
        }
    }

    return [.. allItems.OrderByDescending(i => i.MediaTime)];
}

/// <summary>
/// 加载配置文件，优先加载环境特定配置
/// </summary>
static AppConfig? LoadConfig(string envName)
{
    var baseDir = AppContext.BaseDirectory;

    // 先加载基础配置
    var configPath = Path.Combine(baseDir, "appsettings.json");
    if (!File.Exists(configPath))
        configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
    if (!File.Exists(configPath))
        return null;

    try
    {
        var json = File.ReadAllText(configPath);
        var doc = JsonDocument.Parse(json);
        AppConfig? config = null;

        if (doc.RootElement.TryGetProperty("AppConfig", out var section))
            config = JsonSerializer.Deserialize<AppConfig>(section.GetRawText());
        else
            config = JsonSerializer.Deserialize<AppConfig>(json);

        if (config == null) return null;

        // 尝试加载环境配置并合并（环境配置覆盖基础配置）
        if (!string.IsNullOrEmpty(envName))
        {
            var envPath = Path.Combine(baseDir, $"appsettings.{envName}.json");
            if (!File.Exists(envPath))
                envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"appsettings.{envName}.json");

            if (File.Exists(envPath))
            {
                try
                {
                    var envJson = File.ReadAllText(envPath);
                    var envDoc = JsonDocument.Parse(envJson);
                    if (envDoc.RootElement.TryGetProperty("AppConfig", out var envSection))
                    {
                        var envConfig = JsonSerializer.Deserialize<AppConfig>(envSection.GetRawText());
                        if (envConfig != null)
                            MergeConfig(config, envConfig);
                    }
                }
                catch { /* 环境配置可选 */ }
            }
        }

        return config;
    }
    catch
    {
        return null;
    }
}

/// <summary>
/// 合并配置（envConfig 中非空/非零值覆盖 baseConfig）
/// </summary>
static void MergeConfig(AppConfig baseConfig, AppConfig envConfig)
{
    if (!string.IsNullOrEmpty(envConfig.ApiBaseUrl)) baseConfig.ApiBaseUrl = envConfig.ApiBaseUrl;
    if (!string.IsNullOrEmpty(envConfig.AuthToken)) baseConfig.AuthToken = envConfig.AuthToken;
    if (envConfig.DefaultDeviceIds.Count > 0) baseConfig.DefaultDeviceIds = envConfig.DefaultDeviceIds;
    if (!string.IsNullOrEmpty(envConfig.DownloadDir)) baseConfig.DownloadDir = envConfig.DownloadDir;
    if (envConfig.PageSize > 0) baseConfig.PageSize = envConfig.PageSize;
    if (envConfig.MaxFiles > 0) baseConfig.MaxFiles = envConfig.MaxFiles;
    if (!string.IsNullOrEmpty(envConfig.MinCreationTime)) baseConfig.MinCreationTime = envConfig.MinCreationTime;
    if (!string.IsNullOrEmpty(envConfig.Aws.AccessKey)) baseConfig.Aws.AccessKey = envConfig.Aws.AccessKey;
    if (!string.IsNullOrEmpty(envConfig.Aws.SecretKey)) baseConfig.Aws.SecretKey = envConfig.Aws.SecretKey;
    if (!string.IsNullOrEmpty(envConfig.Aws.Region)) baseConfig.Aws.Region = envConfig.Aws.Region;
    if (!string.IsNullOrEmpty(envConfig.Aws.BucketName)) baseConfig.Aws.BucketName = envConfig.Aws.BucketName;
}

/// <summary>
/// 格式化文件大小
/// </summary>
static string FormatSize(long bytes)
{
    return bytes switch
    {
        < 1024 => $"{bytes}B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1}KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1}MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1}GB"
    };
}

public partial class Program { }
