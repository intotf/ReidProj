using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyDiscern.Models;
using FamilyDiscern.Services;

namespace FamilyDiscern.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private ReidClient _reidClient;

    public MainViewModel()
    {
        var settings = AppSettings.Load();
        ServerUrl = settings.ServerUrl;
        FfmpegPath = settings.FfmpegPath;
        FrameIntervalSeconds = (decimal)settings.FrameIntervalSeconds;
        WCloth = settings.WCloth;
        WHead = settings.WHead;
        WBody = settings.WBody;
        WGait = settings.WGait;
        SelectedGroupId = settings.HistoryGroups.FirstOrDefault() ?? "";
        foreach (var g in settings.HistoryGroups)
            HistoryGroups.Add(g);
        _reidClient = new ReidClient(settings.ServerUrl);
    }

    // === 配置 ===

    [ObservableProperty]
    public partial string ServerUrl { get; set; }

    [ObservableProperty]
    public partial string FfmpegPath { get; set; }

    [ObservableProperty]
    public partial decimal FrameIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial float WCloth { get; set; }

    [ObservableProperty]
    public partial float WHead { get; set; }

    [ObservableProperty]
    public partial float WBody { get; set; }

    [ObservableProperty]
    public partial float WGait { get; set; }

    // === 组管理 ===

    [ObservableProperty]
    public partial string SelectedGroupId { get; set; }

    public ObservableCollection<string> HistoryGroups { get; } = [];

    // === 注册 ===

    [ObservableProperty]
    public partial string EnrollMemberName { get; set; } = "";

    [ObservableProperty]
    public partial string EnrollMp4Path { get; set; } = "";

    // === 成员列表 ===

    public ObservableCollection<FamilyMember> Members { get; } = [];

    // === 识别 ===

    [ObservableProperty]
    public partial string RecognizeMp4Path { get; set; } = "";

    [ObservableProperty]
    public partial string? RecognizeResult { get; set; }

    [ObservableProperty]
    public partial string? VideoCodecInfo { get; set; }

    // === 状态 ===

    [ObservableProperty]
    public partial string StatusText { get; set; } = "就绪";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    // === 命令 ===

    [RelayCommand]
    private void SaveConfig()
    {
        var settings = AppSettings.Load();
        settings.ServerUrl = ServerUrl;
        settings.FfmpegPath = FfmpegPath;
        settings.FrameIntervalSeconds = (double)FrameIntervalSeconds;
        settings.WCloth = WCloth;
        settings.WHead = WHead;
        settings.WBody = WBody;
        settings.WGait = WGait;
        settings.HistoryGroups = [.. HistoryGroups];
        settings.Save();
        _reidClient.UpdateBaseUrl(ServerUrl);
        StatusText = "配置已保存";
    }

    [RelayCommand]
    private async Task RefreshMembersAsync()
    {
        IsBusy = true;
        StatusText = "正在查询所有组的成员...";
        Members.Clear();

        try
        {
            foreach (var groupId in HistoryGroups)
            {
                if (string.IsNullOrWhiteSpace(groupId)) continue;
                try
                {
                    var list = await _reidClient.ListMembersAsync(groupId);
                    var store = LocalMemberStore.Load();

                    foreach (var m in list)
                    {
                        // 用查询时的组名填充
                        if (string.IsNullOrEmpty(m.GroupId))
                            m.GroupId = groupId;

                        // 从本地记录补充额外信息
                        var local = store.Find(m.Id);
                        if (local != null)
                        {
                            m.Mp4Path = local.Mp4Path;
                            m.FrameIntervalSeconds = local.FrameIntervalSeconds;
                            m.RegisterTime = local.RegisterTime;
                        }

                        Members.Add(m);
                    }
                }
                catch
                {
                    // 单个组查询失败不影响其他组
                }
            }
            StatusText = $"查询完成: 共 {Members.Count} 个成员";
        }
        catch (Exception ex)
        {
            StatusText = $"查询失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteMemberAsync(FamilyMember? member)
    {
        if (member == null) return;

        IsBusy = true;
        StatusText = $"正在删除 {member.Name}...";
        try
        {
            var ok = await _reidClient.DeleteMemberAsync(member.GroupId, member.Id);
            if (ok)
            {
                Members.Remove(member);
                // 同步删除本地记录
                var store = LocalMemberStore.Load();
                store.Remove(member.Id);
                StatusText = $"已删除 {member.Name}";
            }
            else
            {
                StatusText = $"删除失败: 未找到成员";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"删除失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EnrollAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedGroupId))
        {
            StatusText = "请输入或选择组名";
            return;
        }
        if (string.IsNullOrWhiteSpace(EnrollMemberName))
        {
            StatusText = "请输入成员名称";
            return;
        }
        if (string.IsNullOrWhiteSpace(EnrollMp4Path) || !File.Exists(EnrollMp4Path))
        {
            StatusText = "请选择有效的 MP4 文件";
            return;
        }

        IsBusy = true;
        StatusText = "正在探测视频编码...";

        try
        {
            var codec = await FfmpegService.DetectCodecAsync(FfmpegPath, EnrollMp4Path);
            if (codec == VideoCodec.Unknown)
            {
                StatusText = "无法识别视频编码，仅支持 H264/H265";
                return;
            }

            StatusText = $"编码: {codec}，正在流式注册...";

            using var ffmpegProcess = FfmpegService.StartRawStream(FfmpegPath, EnrollMp4Path, codec);
            if (ffmpegProcess == null)
            {
                StatusText = "启动 ffmpeg 失败";
                return;
            }

            var result = await _reidClient.EnrollAsync(ffmpegProcess.OutputStream, codec, SelectedGroupId, EnrollMemberName, (double)FrameIntervalSeconds);

            if (result != null)
            {
                // 记录历史组名
                AddHistoryGroup(SelectedGroupId);

                // 保存本地注册记录
                var store = LocalMemberStore.Load();
                store.Add(new LocalMemberRecord
                {
                    MemberId = result.MemberId,
                    Name = result.Name,
                    GroupId = result.GroupId,
                    Mp4Path = EnrollMp4Path,
                    FrameIntervalSeconds = (double)FrameIntervalSeconds,
                    RegisterTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });

                StatusText = $"✓ 注册成功! ID={result.MemberId}, Name={result.Name}, Group={result.GroupId}";
                await RefreshMembersAsync();
            }
            else
            {
                StatusText = "注册失败: 服务返回空结果";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"注册失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RecognizeAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedGroupId))
        {
            StatusText = "请输入或选择组名";
            return;
        }
        if (string.IsNullOrWhiteSpace(RecognizeMp4Path) || !File.Exists(RecognizeMp4Path))
        {
            StatusText = "请选择有效的 MP4 文件";
            return;
        }

        IsBusy = true;
        RecognizeResult = null;
        VideoCodecInfo = null;
        StatusText = "正在探测视频编码...";

        try
        {
            var codec = await FfmpegService.DetectCodecAsync(FfmpegPath, RecognizeMp4Path);
            if (codec == VideoCodec.Unknown)
            {
                StatusText = "无法识别视频编码，仅支持 H264/H265";
                return;
            }

            StatusText = $"编码: {codec}，正在流式识别...";
            VideoCodecInfo = $"视频编码: {codec}";

            using var ffmpegProcess = FfmpegService.StartRawStream(FfmpegPath, RecognizeMp4Path, codec);
            if (ffmpegProcess == null)
            {
                StatusText = "启动 ffmpeg 失败";
                return;
            }

            var result = await _reidClient.RecognizeAsync(ffmpegProcess.OutputStream, codec, SelectedGroupId, (double)FrameIntervalSeconds, WCloth, WHead, WBody, WGait);

            if (result != null)
            {
                if (result.Name == "stranger" || string.IsNullOrEmpty(result.Id))
                {
                    RecognizeResult = "未匹配到已注册成员\n" +
                                      $"最高分: {result.Score:F4}\n" +
                                      $"全身ReID: {result.ClothScore:F4}\n" +
                                      $"头肩ReID: {result.HeadScore:F4}\n" +
                                      $"体型: {result.BodyScore:F4}\n" +
                                      $"步态: {result.GaitScore:F4}";
                    StatusText = "识别完成: 未匹配到已注册成员";
                }
                else
                {
                    RecognizeResult = $"✓ 匹配成功!\n" +
                                      $"姓名: {result.Name}\n" +
                                      $"ID: {result.Id}\n" +
                                      $"总分: {result.Score:F4}\n" +
                                      $"全身ReID: {result.ClothScore:F4}\n" +
                                      $"头肩ReID: {result.HeadScore:F4}\n" +
                                      $"体型: {result.BodyScore:F4}\n" +
                                      $"步态: {result.GaitScore:F4}";
                    StatusText = $"✓ 识别完成: {result.Name} (分数: {result.Score:F4})";
                }
            }
            else
            {
                RecognizeResult = "无结果: 服务返回空（可能视频中未检测到人物）";
                StatusText = "识别完成: 服务返回空结果";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"识别失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddHistoryGroup(string groupId)
    {
        if (!HistoryGroups.Contains(groupId))
            HistoryGroups.Add(groupId);

        var settings = AppSettings.Load();
        settings.AddGroup(groupId);
        settings.Save();
    }
}
