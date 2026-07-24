using System.Text;
using Microsoft.Extensions.Configuration;
using ReIdCli;

Console.OutputEncoding = Encoding.UTF8;

// 加载配置
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

var config = new AppConfig();
configuration.GetSection("AppConfig").Bind(config);

// 解析命令行参数
if (args.Length < 2)
{
    Console.WriteLine("用法: ReIdCli <目标图片路径,多张逗号分隔> <视频目录>");
    Console.WriteLine("示例: ReIdCli \"target1.jpg,target2.jpg\" \"D:\\Videos\"");
    return 1;
}

var targetImages = args[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var videoDir = args[1];

if (!Directory.Exists(videoDir))
{
    Console.Error.WriteLine($"错误: 视频目录不存在 - {videoDir}");
    return 1;
}

foreach (var img in targetImages)
{
    if (!File.Exists(img))
    {
        Console.Error.WriteLine($"错误: 目标图片不存在 - {img}");
        return 1;
    }
}

var featureClient = new ReidFeatureClient(config.ReidServiceUrl);
var frameExtractor = new FrameExtractor(config.FfmpegPath);
var matcher = new PersonMatcher(config.SimilarityThreshold);

Console.WriteLine($"=== ReID CLI ===");
Console.WriteLine($"推理服务: {config.ReidServiceUrl}");
Console.WriteLine($"相似度阈值: {config.SimilarityThreshold}");
Console.WriteLine($"目标图片: {targetImages.Length} 张");
Console.WriteLine($"视频目录: {videoDir}");
Console.WriteLine();

// 第 1 步：提取目标人物特征并缓存到内存
Console.WriteLine("[1/4] 提取目标人物特征...");
var targetFeatures = new List<TargetPerson>();

for (int i = 0; i < targetImages.Length; i++)
{
    var imgPath = targetImages[i];
    Console.Write($"  处理 {Path.GetFileName(imgPath)}...");

    var imageBytes = await File.ReadAllBytesAsync(imgPath);
    var detections = await featureClient.DetectAsync(imageBytes);

    if (detections.Count == 0)
    {
        Console.WriteLine(" 未检测到人物，跳过");
        continue;
    }

    // 取置信度最高的人物作为目标
    var best = detections.OrderByDescending(d => d.Confidence).First();
    targetFeatures.Add(new TargetPerson
    {
        Name = Path.GetFileNameWithoutExtension(imgPath),
        ImagePath = imgPath,
        Features = best.Features,
        Confidence = best.Confidence
    });

    Console.WriteLine($" 完成 (置信度: {best.Confidence:F3})");
}

if (targetFeatures.Count == 0)
{
    Console.Error.WriteLine("错误: 没有有效的目标人物特征");
    return 1;
}

Console.WriteLine($"  共缓存 {targetFeatures.Count} 个目标人物特征");
Console.WriteLine();

// 第 2 步：遍历视频，抽帧匹配
Console.WriteLine("[2/4] 扫描视频文件...");
var videoFiles = Directory.GetFiles(videoDir, "*.*", SearchOption.TopDirectoryOnly)
    .Where(f => AppConfig.VideoExtensions.Contains(Path.GetExtension(f).ToLower()))
    .OrderBy(f => f)
    .ToArray();

if (videoFiles.Length == 0)
{
    Console.Error.WriteLine($"错误: 视频目录下没有视频文件");
    return 1;
}

Console.WriteLine($"  找到 {videoFiles.Length} 个视频文件");
Console.WriteLine();

Console.WriteLine("[3/4] 抽帧并匹配...");
var allResults = new List<MatchResult>();

foreach (var videoPath in videoFiles)
{
    Console.WriteLine($"  处理视频: {Path.GetFileName(videoPath)}");

    // 创建临时目录存放抽帧图片
    var tempDir = Path.Combine(Path.GetTempPath(), "reidcli_frames_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tempDir);

    try
    {
        // ffmpeg 抽帧: 每秒1帧, 640x360
        var success = await frameExtractor.ExtractFramesAsync(videoPath, tempDir);
        if (!success)
        {
            Console.WriteLine("    ffmpeg 抽帧失败，跳过");
            continue;
        }

        var frameFiles = Directory.GetFiles(tempDir, "*.jpg").OrderBy(f => f).ToArray();
        Console.WriteLine($"    抽取 {frameFiles.Length} 帧");

        bool matched = false;
        foreach (var framePath in frameFiles)
        {
            var frameBytes = await File.ReadAllBytesAsync(framePath);
            var detections = await featureClient.DetectAsync(frameBytes);

            if (detections.Count == 0) continue;

            // 对帧中每个人物与所有目标比较
            var result = matcher.FindBestMatch(detections, targetFeatures, videoPath, framePath);
            if (result != null)
            {
                allResults.Add(result);
                Console.WriteLine($"    ✓ 匹配! 帧={Path.GetFileName(framePath)}, " +
                                  $"目标={result.TargetName}, 相似度={result.Similarity:F4}");
                matched = true;
                break; // 达到阈值，终止该视频的抽帧
            }
        }

        if (!matched)
        {
            Console.WriteLine("    × 未找到匹配目标");
        }
    }
    finally
    {
        // 清理临时文件
        try { Directory.Delete(tempDir, true); } catch { }
    }
}

// 第 4 步：输出结果
Console.WriteLine();
Console.WriteLine("[4/4] 匹配结果汇总:");
Console.WriteLine(new string('─', 80));

if (allResults.Count == 0)
{
    Console.WriteLine("未找到任何匹配结果。");
}
else
{
    Console.WriteLine($"{"目标",-15} {"视频",-25} {"帧位置",-20} {"相似度",-10}");
    Console.WriteLine(new string('─', 80));

    foreach (var r in allResults.OrderByDescending(r => r.Similarity))
    {
        Console.WriteLine($"{r.TargetName,-15} {Path.GetFileName(r.VideoPath),-25} " +
                          $"{Path.GetFileName(r.FramePath),-20} {r.Similarity:F4}");
    }
}

Console.WriteLine(new string('─', 80));
Console.WriteLine($"总计: {allResults.Count} 个匹配");

return 0;
