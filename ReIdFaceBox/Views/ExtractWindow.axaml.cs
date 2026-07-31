using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ReIdFaceBox.Models;
using ReIdFaceBox.ViewModels;

namespace ReIdFaceBox.Views;

public partial class ExtractWindow : Window
{
    private readonly IReadOnlyList<ResultImageItem> _items;

    public ExtractWindow()
    {
        InitializeComponent();
        _items = [];
        LoadSettings();
    }

    public ExtractWindow(IReadOnlyList<ResultImageItem> items)
    {
        InitializeComponent();
        _items = items;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = AppSettings.Load();
        ChkFace.IsChecked = settings.ExtractCheckFace;
        FaceMinWidth.Value = settings.ExtractFaceMinWidth;
        FaceMinHeight.Value = settings.ExtractFaceMinHeight;
        ChkBody.IsChecked = settings.ExtractCheckBody;
        BodyMinWidth.Value = settings.ExtractBodyMinWidth;
        BodyMinHeight.Value = settings.ExtractBodyMinHeight;
        ChkCropBbox.IsChecked = settings.ExtractCropBbox;
        CmbLogic.SelectedIndex = settings.ExtractConditionLogic == "Or" ? 1 : 0;
        TxtOutputDir.Text = settings.ExtractOutputDir;

        UpdateLogicVisibility();
        ChkFace.IsCheckedChanged += (_, _) => UpdateLogicVisibility();
        ChkBody.IsCheckedChanged += (_, _) => UpdateLogicVisibility();
    }

    private void UpdateLogicVisibility()
    {
        PnlLogic.IsVisible = ChkFace.IsChecked == true && ChkBody.IsChecked == true;
    }

    private void SaveSettings()
    {
        var settings = AppSettings.Load();
        settings.ExtractCheckFace = ChkFace.IsChecked == true;
        settings.ExtractFaceMinWidth = (int)(FaceMinWidth.Value ?? 0);
        settings.ExtractFaceMinHeight = (int)(FaceMinHeight.Value ?? 0);
        settings.ExtractCheckBody = ChkBody.IsChecked == true;
        settings.ExtractBodyMinWidth = (int)(BodyMinWidth.Value ?? 0);
        settings.ExtractBodyMinHeight = (int)(BodyMinHeight.Value ?? 0);
        settings.ExtractCropBbox = ChkCropBbox.IsChecked == true;
        settings.ExtractConditionLogic = CmbLogic.SelectedIndex == 1 ? "Or" : "And";
        settings.ExtractOutputDir = TxtOutputDir.Text?.Trim() ?? "";
        settings.Save();
    }

    private async void OnBrowseDir(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择保存目录",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (path != null)
                TxtOutputDir.Text = path;
        }
    }

    private void OnExtract(object? sender, RoutedEventArgs e)
    {
        var outputDir = TxtOutputDir.Text?.Trim();
        if (string.IsNullOrEmpty(outputDir))
        {
            TxtStatus.Text = "请选择保存目录";
            return;
        }

        // 保存当前设置到配置文件
        SaveSettings();

        Directory.CreateDirectory(outputDir);

        var checkFace = ChkFace.IsChecked == true;
        var checkBody = ChkBody.IsChecked == true;
        var cropBbox = ChkCropBbox.IsChecked == true;
        var useOrLogic = CmbLogic.SelectedIndex == 1; // 0=And, 1=Or
        var faceMinW = (int)(FaceMinWidth.Value ?? 0);
        var faceMinH = (int)(FaceMinHeight.Value ?? 0);
        var bodyMinW = (int)(BodyMinWidth.Value ?? 0);
        var bodyMinH = (int)(BodyMinHeight.Value ?? 0);

        int savedCount = 0;
        int processedCount = 0;

        foreach (var item in _items)
        {
            processedCount++;
            TxtStatus.Text = $"处理中: {item.FileName} ({processedCount}/{_items.Count})";

            if (item.Detections.Count == 0) continue;

            var matchedDetections = new List<PersonDetection>();

            foreach (var det in item.Detections)
            {
                bool bodyOk = true;
                bool faceOk = true;

                // 人型尺寸判定（未勾选时不参与判定）
                if (checkBody && (bodyMinW > 0 || bodyMinH > 0))
                {
                    if (det.Bbox == null || det.Bbox.Width < bodyMinW || det.Bbox.Height < bodyMinH)
                        bodyOk = false;
                }

                // 人脸尺寸判定（未勾选时不参与判定）
                if (checkFace && (faceMinW > 0 || faceMinH > 0))
                {
                    if (det.Face?.Bbox == null || det.Face.Bbox.Width < faceMinW || det.Face.Bbox.Height < faceMinH)
                        faceOk = false;
                }

                // 判定逻辑：只有两个都勾选时 And/Or 才生效
                bool matched;
                if (checkBody && checkFace)
                {
                    // 两个都启用，使用 And/Or 逻辑
                    matched = useOrLogic ? (bodyOk || faceOk) : (bodyOk && faceOk);
                }
                else if (checkBody)
                {
                    matched = bodyOk;
                }
                else if (checkFace)
                {
                    matched = faceOk;
                }
                else
                {
                    matched = true; // 都没勾选，不过滤
                }

                if (matched)
                {
                    matchedDetections.Add(det);
                }
            }

            if (matchedDetections.Count == 0) continue;

            if (cropBbox)
            {
                // 按每个 bbox 裁剪保存
                using var ms = new MemoryStream(item.ImageBytes);
                var bitmap = new Bitmap(ms);

                for (int i = 0; i < matchedDetections.Count; i++)
                {
                    var det = matchedDetections[i];
                    if (det.Bbox == null || det.Bbox.Width <= 0 || det.Bbox.Height <= 0)
                        continue;

                    // Clamp bbox to image bounds
                    var x = Math.Max(0, det.Bbox.X);
                    var y = Math.Max(0, det.Bbox.Y);
                    var w = Math.Min(det.Bbox.Width, bitmap.PixelSize.Width - x);
                    var h = Math.Min(det.Bbox.Height, bitmap.PixelSize.Height - y);
                    if (w <= 0 || h <= 0) continue;

                    var cropped = new CroppedBitmap(bitmap, new PixelRect(x, y, w, h));

                    var name = Path.GetFileNameWithoutExtension(item.FileName);
                    var ext = Path.GetExtension(item.FileName);
                    var savePath = Path.Combine(outputDir, $"{name}_p{i}{ext}");

                    // CroppedBitmap 不能直接 Save，需要渲染到 RenderTargetBitmap
                    var renderTarget = new RenderTargetBitmap(new PixelSize(w, h));
                    using (var ctx = renderTarget.CreateDrawingContext())
                    {
                        ctx.DrawImage(cropped, new Rect(0, 0, w, h));
                    }

                    using var fs = new FileStream(savePath, FileMode.Create);
                    renderTarget.Save(fs, new JpegBitmapEncoderOptions());
                    renderTarget.Dispose();
                    savedCount++;
                }

                bitmap.Dispose();
            }
            else
            {
                // 保存原始完整图片
                var savePath = Path.Combine(outputDir, item.FileName);
                if (File.Exists(savePath))
                {
                    var name = Path.GetFileNameWithoutExtension(item.FileName);
                    var ext = Path.GetExtension(item.FileName);
                    savePath = Path.Combine(outputDir, $"{name}_{savedCount}{ext}");
                }

                File.WriteAllBytes(savePath, item.ImageBytes);
                savedCount++;
            }
        }

        TxtStatus.Text = $"✓ 提取完成! 共 {_items.Count} 张，已保存 {savedCount} 个文件到 {outputDir}";
        TxtStatus.Foreground = Avalonia.Media.Brushes.Green;
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
