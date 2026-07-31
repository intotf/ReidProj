using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReIdFaceBox.Models;
using ReIdFaceBox.Services;

namespace ReIdFaceBox.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private DetectClient _detectClient;
    private CancellationTokenSource? _cts;

    public MainViewModel()
    {
        var settings = AppSettings.Load();
        ReidServiceUrl = settings.ReidServiceUrl;
        FfmpegPath = settings.FfmpegPath;
        FfmpegArgs = settings.FfmpegArgs;
        DetectionFlags = settings.DetectionFlags;
        GroupId = settings.GroupId;
        SimilarityThreshold = settings.SimilarityThreshold;
        _detectClient = new DetectClient(settings.ReidServiceUrl);
    }

    // === 配置属性 ===

    [ObservableProperty]
    public partial string ReidServiceUrl { get; set; }

    [ObservableProperty]
    public partial string FfmpegPath { get; set; }

    [ObservableProperty]
    public partial string FfmpegArgs { get; set; }

    [ObservableProperty]
    public partial int DetectionFlags { get; set; }

    [ObservableProperty]
    public partial string GroupId { get; set; }

    [ObservableProperty]
    public partial float SimilarityThreshold { get; set; }

    // === 状态属性 ===

    [ObservableProperty]
    public partial string StatusText { get; set; } = "就绪";

    [ObservableProperty]
    public partial bool IsProcessing { get; set; }

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial int ProcessedCount { get; set; }

    // === 结果列表 ===

    public ObservableCollection<ResultImageItem> ResultImages { get; } = [];

    // === 命令 ===

    [RelayCommand]
    private void SaveConfig()
    {
        var settings = AppSettings.Load(); // 先加载现有配置，保留提取设置等
        settings.ReidServiceUrl = ReidServiceUrl;
        settings.FfmpegPath = FfmpegPath;
        settings.FfmpegArgs = FfmpegArgs;
        settings.DetectionFlags = DetectionFlags;
        settings.GroupId = GroupId;
        settings.SimilarityThreshold = SimilarityThreshold;
        settings.Save();
        _detectClient.UpdateBaseUrl(ReidServiceUrl);
        StatusText = "配置已保存";
    }

    [RelayCommand]
    private void LoadConfig()
    {
        var settings = AppSettings.Load();
        ReidServiceUrl = settings.ReidServiceUrl;
        FfmpegPath = settings.FfmpegPath;
        FfmpegArgs = settings.FfmpegArgs;
        DetectionFlags = settings.DetectionFlags;
        GroupId = settings.GroupId;
        SimilarityThreshold = settings.SimilarityThreshold;
        _detectClient.UpdateBaseUrl(ReidServiceUrl);
        StatusText = "配置已加载";
    }

    [RelayCommand]
    private void CancelProcessing()
    {
        _cts?.Cancel();
        StatusText = "已取消";
    }

    /// <summary>
    /// 对指定图片进行目标对比（使用原始图片，不含 bbox）
    /// </summary>
    public async Task<List<PersonRecognition>> RecognizeAsync(byte[] imageBytes)
    {
        return await _detectClient.RecognizeAsync(imageBytes, GroupId, SimilarityThreshold, DetectionFlags);
    }

    /// <summary>
    /// 处理选中的图片列表
    /// </summary>
    public async Task ProcessImagesAsync(string[] imagePaths)
    {
        if (imagePaths.Length == 0) return;

        IsProcessing = true;
        _cts = new CancellationTokenSource();
        ResultImages.Clear();
        TotalCount = imagePaths.Length;
        ProcessedCount = 0;

        try
        {
            foreach (var path in imagePaths)
            {
                if (_cts.Token.IsCancellationRequested) break;

                StatusText = $"处理中: {Path.GetFileName(path)} ({ProcessedCount + 1}/{TotalCount})";

                var imageBytes = await File.ReadAllBytesAsync(path, _cts.Token);
                List<PersonDetection> detections;

                try
                {
                    detections = await _detectClient.DetectAsync(imageBytes, DetectionFlags, _cts.Token);
                }
                catch (Exception ex)
                {
                    StatusText = $"请求失败: {ex.Message}";
                    detections = [];
                }

                var rendered = ImageRenderer.RenderDetections(imageBytes, detections);
                ResultImages.Add(new ResultImageItem
                {
                    FileName = Path.GetFileName(path),
                    Image = rendered,
                    ImageBytes = imageBytes,
                    PersonCount = detections.Count,
                    Detections = detections
                });

                ProcessedCount++;
            }

            StatusText = $"完成: 共处理 {ProcessedCount} 张图片";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"已取消，已处理 {ProcessedCount}/{TotalCount}";
        }
        catch (Exception ex)
        {
            StatusText = $"错误: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 处理视频文件：ffmpeg 抽帧到内存后逐张检测（不落盘）
    /// </summary>
    public async Task ProcessVideoAsync(string videoPath)
    {
        IsProcessing = true;
        _cts = new CancellationTokenSource();
        ResultImages.Clear();
        ProcessedCount = 0;
        StatusText = "正在抽帧（内存模式）...";

        try
        {
            var frames = await FrameExtractor.ExtractToMemoryAsync(FfmpegPath, FfmpegArgs, videoPath, _cts.Token);
            if (frames == null || frames.Count == 0)
            {
                StatusText = "ffmpeg 抽帧失败或未抽取到帧，请检查路径和参数";
                return;
            }

            TotalCount = frames.Count;
            StatusText = $"抽取到 {frames.Count} 帧，开始检测...";

            for (int i = 0; i < frames.Count; i++)
            {
                if (_cts.Token.IsCancellationRequested) break;

                var frameBytes = frames[i];
                var videoName = Path.GetFileNameWithoutExtension(videoPath);
                var frameName = $"{videoName}_frame_{i + 1:D4}.jpg";
                StatusText = $"检测中: {frameName} ({i + 1}/{TotalCount})";

                List<PersonDetection> detections;

                try
                {
                    detections = await _detectClient.DetectAsync(frameBytes, DetectionFlags, _cts.Token);
                }
                catch (Exception ex)
                {
                    StatusText = $"请求失败: {ex.Message}";
                    detections = [];
                }

                var rendered = ImageRenderer.RenderDetections(frameBytes, detections);
                ResultImages.Add(new ResultImageItem
                {
                    FileName = frameName,
                    Image = rendered,
                    ImageBytes = frameBytes,
                    PersonCount = detections.Count,
                    Detections = detections
                });

                ProcessedCount++;
            }

            StatusText = $"完成: 共处理 {ProcessedCount} 帧";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"已取消，已处理 {ProcessedCount}/{TotalCount}";
        }
        catch (Exception ex)
        {
            StatusText = $"错误: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }
}

public class ResultImageItem : ObservableObject
{
    public required string FileName { get; set; }

    private Bitmap _image = null!;
    public required Bitmap Image
    {
        get => _image;
        set => SetProperty(ref _image, value);
    }

    /// <summary>
    /// 原始图片字节（不含 bbox），用于目标对比和另存为原图
    /// </summary>
    public required byte[] ImageBytes { get; set; }
    public int PersonCount { get; set; }
    public List<PersonDetection> Detections { get; set; } = [];

    private string? _recognizeResult;
    public string? RecognizeResult
    {
        get => _recognizeResult;
        set
        {
            if (SetProperty(ref _recognizeResult, value))
            {
                // 对比结果变化时，重新渲染图片（包含 bbox + 文字）
                Image = Services.ImageRenderer.RenderDetections(ImageBytes, Detections, value);
            }
        }
    }
}
