using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FamilyDiscern.ViewModels;

namespace FamilyDiscern.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    private async void OnSelectEnrollMp4(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择注册用视频",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("视频文件") { Patterns = ["*.mp4", "*.mkv", "*.avi", "*.mov"] }
            ]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
                ViewModel.EnrollMp4Path = path;
        }
    }

    private async void OnSelectRecognizeMp4(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择识别用视频",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("视频文件") { Patterns = ["*.mp4", "*.mkv", "*.avi", "*.mov"] }
            ]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
                ViewModel.RecognizeMp4Path = path;
        }
    }
}
