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
        HighConfidenceThreshold = settings.HighConfidenceThreshold;
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

    [ObservableProperty]
    public partial float HighConfidenceThreshold { get; set; }

    // === 组管理 ===

    [ObservableProperty]
    public partial string SelectedGroupId { get; set; }

    public ObservableCollection<string> HistoryGroups { get; } = [];

    // === 注册 ===

    [ObservableProperty]
    public partial string EnrollMemberName { get; set; } = "";

    [ObservableProperty]
    public partial string EnrollMp4Path { get; set; } = "";

    [ObservableProperty]
    public partial string EnrollMp4Paths { get; set; } = "";

    // === 合并去重 ===

    [ObservableProperty]
    public partial string MergeTargetMemberId { get; set; } = "";

    [ObservableProperty]
    public partial string MergeMemberIds { get; set; } = "";

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
        settings.HighConfidenceThreshold = HighConfidenceThreshold;
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
            var registeredByGroup = new Dictionary<string, IReadOnlyCollection<FamilyMember>>(
                StringComparer.Ordinal);
            var remoteMembers = new List<FamilyMember>();
            var failedGroupCount = 0;

            foreach (var groupId in HistoryGroups
                         .Where(g => !string.IsNullOrWhiteSpace(g))
                         .Distinct(StringComparer.Ordinal)
                         .ToArray())
            {
                try
                {
                    var list = await _reidClient.ListMembersAsync(groupId);
                    foreach (var member in list)
                    {
                        member.GroupId = groupId;
                    }

                    registeredByGroup[groupId] = list;
                    remoteMembers.AddRange(list);
                }
                catch
                {
                    // 查询失败的组不参与同步，避免网络故障导致本地记录被误删。
                    failedGroupCount++;
                }
            }

            // 远端注册列表是权威数据；同步会删除成功查询组中的陈旧本地记录。
            var store = LocalMemberStore.Synchronize(registeredByGroup);
            foreach (var member in remoteMembers)
            {
                var local = store.Find(member.GroupId, member.Id);
                if (local != null)
                {
                    member.Mp4Path = local.Mp4Path;
                    member.FrameIntervalSeconds = local.FrameIntervalSeconds;
                    member.RegisterTime = local.RegisterTime;
                }

                Members.Add(member);
            }

            StatusText = failedGroupCount == 0
                ? $"查询并同步完成: 共 {Members.Count} 个成员"
                : $"同步完成: 共 {Members.Count} 个成员，{failedGroupCount} 个组查询失败";
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
                LocalMemberStore.Remove(member.GroupId, member.Id);
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

            var result = await _reidClient.EnrollAsync(ffmpegProcess.OutputStream, SelectedGroupId, EnrollMemberName, (double)FrameIntervalSeconds);

            if (result != null)
            {
                // 记录历史组名
                AddHistoryGroup(SelectedGroupId);

                // 同一成员重复注册时更新本地元数据，不产生重复记录。
                LocalMemberStore.Upsert(new LocalMemberRecord
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
    private async Task EnrollMemberBatchAsync()
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

        var paths = EnrollMp4Paths
            .Split([';', '，', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => File.Exists(p))
            .ToArray();
        if (paths.Length == 0)
        {
            StatusText = "请选择至少 1 个有效的视频文件";
            return;
        }

        IsBusy = true;
        StatusText = $"正在为 {paths.Length} 段视频转裸流并批量注册...";
        var tempFiles = new List<string>();
        try
        {
            foreach (var path in paths)
            {
                var codec = await FfmpegService.DetectCodecAsync(FfmpegPath, path);
                if (codec == VideoCodec.Unknown)
                {
                    StatusText = $"无法识别视频编码，仅支持 H264/H265: {path}";
                    return;
                }

                var raw = await FfmpegService.ConvertToRawFileAsync(FfmpegPath, path, codec);
                if (raw == null)
                {
                    StatusText = $"ffmpeg 转裸流失败: {path}";
                    return;
                }
                tempFiles.Add(raw);
            }

            var result = await _reidClient.EnrollBatchAsync(
                tempFiles, SelectedGroupId, EnrollMemberName, (double)FrameIntervalSeconds, append: true);
            if (result == null)
            {
                StatusText = "批量注册失败: 服务返回空结果";
                return;
            }

            AddHistoryGroup(SelectedGroupId);
            LocalMemberStore.Upsert(new LocalMemberRecord
            {
                MemberId = result.MemberId,
                Name = result.Name,
                GroupId = result.GroupId,
                Mp4Path = paths[0],
                FrameIntervalSeconds = (double)FrameIntervalSeconds,
                RegisterTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });

            StatusText = $"✓ 批量注册成功! ID={result.MemberId}, Name={result.Name}, 融合段数={result.SegmentCount}";
            await RefreshMembersAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"批量注册失败: {ex.Message}";
        }
        finally
        {
            foreach (var f in tempFiles)
            {
                try
                {
                    File.Delete(f);
                }
                catch
                {
                    // 忽略临时文件清理失败
                }
            }
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task MergeMembersAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedGroupId))
        {
            StatusText = "请输入或选择组名";
            return;
        }
        if (string.IsNullOrWhiteSpace(MergeTargetMemberId))
        {
            StatusText = "请输入目标成员 ID";
            return;
        }

        var ids = MergeMemberIds
            .Split([';', '，', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        if (ids.Length == 0)
        {
            StatusText = "请输入待合并成员 ID（多个用分号/逗号分隔）";
            return;
        }

        IsBusy = true;
        StatusText = "正在合并成员...";
        try
        {
            var members = await _reidClient.MergeMembersAsync(SelectedGroupId, MergeTargetMemberId, ids);
            LocalMemberStore.SynchronizeGroup(SelectedGroupId, members);
            StatusText = $"✓ 合并完成，组 {SelectedGroupId} 现有 {members.Count} 个成员";
            await RefreshMembersAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"合并失败: {ex.Message}";
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

            var result = await _reidClient.RecognizeAsync(ffmpegProcess.OutputStream, SelectedGroupId, (double)FrameIntervalSeconds, WCloth, WHead, WBody, WGait, HighConfidenceThreshold);

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
