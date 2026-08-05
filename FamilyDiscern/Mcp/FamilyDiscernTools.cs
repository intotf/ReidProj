using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FamilyDiscern.Models;
using FamilyDiscern.Services;
using ModelContextProtocol.Server;

namespace FamilyDiscern.Mcp;

[McpServerToolType]
public class FamilyDiscernTools
{
    private static AppSettings GetSettings() => AppSettings.Load();

    /// <summary>
    /// 获取有效的 groupId，为空时使用配置文件第一个组名
    /// </summary>
    private static string ResolveGroupId(string? groupId, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(groupId))
            return groupId;
        return settings.HistoryGroups.FirstOrDefault() ?? "group1";
    }

    /// <summary>
    /// 注册家庭成员：将 MP4 视频转为裸流后注册到指定组
    /// </summary>
    [McpServerTool, Description("注册家庭成员，输入 MP4 视频路径、组名(可选,默认使用配置第一个组)和成员名")]
    public static async Task<string> EnrollMember(string mp4Path, string memberName, string? groupId = null)
    {
        var settings = GetSettings();
        groupId = ResolveGroupId(groupId, settings);

        if (!File.Exists(mp4Path))
            return $"错误: 文件不存在 - {mp4Path}";

        var codec = await FfmpegService.DetectCodecAsync(settings.FfmpegPath, mp4Path);
        if (codec == VideoCodec.Unknown)
            return "错误: 无法识别视频编码，仅支持 H264/H265";

        using var ffmpegProcess = FfmpegService.StartRawStream(settings.FfmpegPath, mp4Path, codec);
        if (ffmpegProcess == null)
            return "错误: 启动 ffmpeg 失败";

        using var client = new ReidClient(settings.ServerUrl);
        var result = await client.EnrollAsync(ffmpegProcess.OutputStream, codec, groupId, memberName, settings.FrameIntervalSeconds);

        if (result != null)
        {
            settings.AddGroup(groupId);
            settings.Save();

            // 新增或更新本地注册记录，避免重复成员。
            LocalMemberStore.Upsert(new LocalMemberRecord
            {
                MemberId = result.MemberId,
                Name = result.Name,
                GroupId = result.GroupId,
                Mp4Path = mp4Path,
                FrameIntervalSeconds = settings.FrameIntervalSeconds,
                RegisterTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });

            return $"注册成功: ID={result.MemberId}, Name={result.Name}, Group={result.GroupId}";
        }

        return "注册失败: 服务返回空结果";
    }

    /// <summary>
    /// 识别视频中的人物
    /// </summary>
    [McpServerTool, Description("识别 MP4 视频中的人物，返回匹配结果。groupId 可选，默认使用配置第一个组")]
    public static async Task<string> RecognizeVideo(string mp4Path, string? groupId = null)
    {
        var settings = GetSettings();
        groupId = ResolveGroupId(groupId, settings);

        if (!File.Exists(mp4Path))
            return $"错误: 文件不存在 - {mp4Path}";

        var codec = await FfmpegService.DetectCodecAsync(settings.FfmpegPath, mp4Path);
        if (codec == VideoCodec.Unknown)
            return "错误: 无法识别视频编码，仅支持 H264/H265";

        using var ffmpegProcess = FfmpegService.StartRawStream(settings.FfmpegPath, mp4Path, codec);
        if (ffmpegProcess == null)
            return "错误: 启动 ffmpeg 失败";

        using var client = new ReidClient(settings.ServerUrl);
        var result = await client.RecognizeAsync(
            ffmpegProcess.OutputStream, codec, groupId,
            settings.FrameIntervalSeconds,
            settings.WCloth, settings.WHead, settings.WBody, settings.WGait);

        if (result != null)
        {
            return $"识别结果:\n" +
                   $"  姓名: {result.Name}\n" +
                   $"  ID: {result.Id}\n" +
                   $"  组: {groupId}\n" +
                   $"  总分: {result.Score:F4}\n" +
                   $"  全身ReID: {result.ClothScore:F4}\n" +
                   $"  头肩ReID: {result.HeadScore:F4}\n" +
                   $"  体型: {result.BodyScore:F4}\n" +
                   $"  步态: {result.GaitScore:F4}";
        }

        return "识别完成: 无匹配结果";
    }

    /// <summary>
    /// 列出组内所有成员，并将远端注册列表同步到 members.json
    /// </summary>
    [McpServerTool, Description("列出指定组的所有家庭成员并同步本地记录。groupId 可选，默认使用配置第一个组")]
    public static async Task<string> ListMembers(string? groupId = null)
    {
        var settings = GetSettings();
        groupId = ResolveGroupId(groupId, settings);

        using var client = new ReidClient(settings.ServerUrl);
        var members = await client.ListMembersAsync(groupId);

        // 即使远端返回空列表也要同步，以删除该组的陈旧本地记录。
        LocalMemberStore.SynchronizeGroup(groupId, members);

        if (members.Count == 0)
            return $"组 {groupId} 中没有成员";

        var lines = members.Select(m => $"  {m.Id} - {m.Name}");
        return $"组 {groupId} 成员 ({members.Count}):\n{string.Join("\n", lines)}";
    }

    /// <summary>
    /// 删除指定成员
    /// </summary>
    [McpServerTool, Description("删除指定组的指定成员。groupId 可选，默认使用配置第一个组")]
    public static async Task<string> DeleteMember(string memberId, string? groupId = null)
    {
        var settings = GetSettings();
        groupId = ResolveGroupId(groupId, settings);

        using var client = new ReidClient(settings.ServerUrl);
        var ok = await client.DeleteMemberAsync(groupId, memberId);
        if (ok)
        {
            LocalMemberStore.Remove(groupId, memberId);
            return $"已删除成员 {memberId} (组: {groupId})";
        }

        return $"删除失败: 未找到成员 {memberId} (组: {groupId})";
    }
}
