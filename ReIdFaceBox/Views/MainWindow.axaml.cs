using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReIdFaceBox.ViewModels;

namespace ReIdFaceBox.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    private async void OnSelectImages(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图片",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("图片文件")
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp"]
                }
            ]
        });

        if (files.Count == 0) return;

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => p != null)
            .ToArray();

        await ViewModel.ProcessImagesAsync(paths!);
    }

    private async void OnSelectVideo(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择视频文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("视频文件")
                {
                    Patterns = ["*.mp4", "*.avi", "*.mkv", "*.mov", "*.flv", "*.wmv", "*.webm", "*.ts"]
                }
            ]
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (path == null) return;

        await ViewModel.ProcessVideoAsync(path);
    }

    private async void OnSaveImageAs(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not ResultImageItem item)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存为",
            SuggestedFileName = item.FileName,
            FileTypeChoices =
            [
                new FilePickerFileType("JPEG 图片") { Patterns = ["*.jpg"] },
                new FilePickerFileType("PNG 图片") { Patterns = ["*.png"] },
                new FilePickerFileType("所有文件") { Patterns = ["*.*"] }
            ]
        });

        if (file == null) return;

        var path = file.TryGetLocalPath();
        if (path == null) return;

        // 保存包含 bbox 框的渲染图片
        using var fs = new FileStream(path, FileMode.Create);
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            item.Image.Save(fs, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        else
            item.Image.Save(fs, new Avalonia.Media.Imaging.JpegBitmapEncoderOptions());
    }

    private async void OnRecognize(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not ResultImageItem item)
            return;

        item.RecognizeResult = "对比中...";

        try
        {
            var results = await ViewModel.RecognizeAsync(item.ImageBytes);

            if (results.Count == 0)
            {
                item.RecognizeResult = "未匹配到目标";
            }
            else
            {
                var lines = results.Select(r =>
                    $"[{r.Name}] ReID: {r.ReidSimilarity:F4}, Face: {r.FaceSimilarity:F4} 目标图: {r.Id}");
                item.RecognizeResult = string.Join("\n", lines);
            }
        }
        catch (Exception ex)
        {
            item.RecognizeResult = $"对比失败: {ex.Message}";
        }
    }

    private async void OnRecognizeAll(object? sender, RoutedEventArgs e)
    {
        var items = ViewModel.ResultImages;
        if (items.Count == 0) return;

        ViewModel.StatusText = "全部对比中...";

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            item.RecognizeResult = "对比中...";
            ViewModel.StatusText = $"对比中: {item.FileName} ({i + 1}/{items.Count})";

            try
            {
                var results = await ViewModel.RecognizeAsync(item.ImageBytes);

                if (results.Count == 0)
                {
                    item.RecognizeResult = "未匹配到目标";
                }
                else
                {
                    var lines = results.Select(r =>
                        $"目标图: {r.Id}  [{r.Name}] ReID: {r.ReidSimilarity:F4}, Face: {r.FaceSimilarity:F4}");
                    item.RecognizeResult = string.Join("\n", lines);
                }
            }
            catch (Exception ex)
            {
                item.RecognizeResult = $"对比失败: {ex.Message}";
            }
        }

        ViewModel.StatusText = $"全部对比完成: {items.Count} 张";
    }

    private void OnExtractTargets(object? sender, RoutedEventArgs e)
    {
        var items = ViewModel.ResultImages;
        if (items.Count == 0)
        {
            ViewModel.StatusText = "没有可提取的图片，请先加载图片或视频";
            return;
        }

        var extractWindow = new ExtractWindow(items);
        extractWindow.ShowDialog(this);
    }
}
