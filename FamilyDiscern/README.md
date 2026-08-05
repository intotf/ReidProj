# FamilyDiscern

基于 ReidFeature 服务的家庭成员识别管理工具，提供 Avalonia 桌面 GUI 和本地 MCP Server 两种使用方式。

## 功能概览

| 功能 | 说明 |
|---|---|
| 成员注册 | 选择 MP4 视频 → 自动探测编码(H264/H265) → 流式传输裸流 → 注册到指定组 |
| 成员管理 | 查看所有组的成员列表、删除成员 |
| 视频识别 | 选择 MP4 → 流式传输 → 四维特征融合匹配 → 返回最佳匹配结果 |
| MCP Server | 同进程启动，暴露 4 个工具供 AI Agent 调用 |

## 技术特点

- **流式传输**：ffmpeg stdout → StreamContent → HTTP chunked transfer → ReidFeature 服务，全程不缓存完整视频到内存
- **自动编码探测**：通过 ffprobe 识别视频编码，自动选择 H264 或 H265 接口
- **本地记录**：注册信息（视频路径、帧间隔、注册时间）保存到 `members.json`，与服务端数据合并展示
- **配置持久化**：所有参数（服务地址、ffmpeg 路径、帧间隔、权重、组名列表）保存在 `appsettings.json`

## 配置文件

`appsettings.json`：

```json
{
  "ServerUrl": "http://localhost:9000",
  "FfmpegPath": "G:\\Tools\\ffmpeg\\ffmpeg.exe",
  "FrameIntervalSeconds": 0.5,
  "WCloth": 0.20,
  "WHead": 0.30,
  "WBody": 0.30,
  "WGait": 0.20,
  "HistoryGroups": ["group1"]
}
```

| 参数 | 说明 |
|---|---|
| ServerUrl | ReidFeature 服务地址 |
| FfmpegPath | ffmpeg 可执行文件路径（同目录需有 ffprobe） |
| FrameIntervalSeconds | 注册/识别时视频抽帧间隔（秒），如 0.5 表示每 0.5 秒取一帧 |
| WCloth | 全身 ReID 权重 |
| WHead | 头肩 ReID 权重 |
| WBody | 体型标量权重 |
| WGait | 步态标量权重 |
| HistoryGroups | 历史组名列表，注册时自动追加新组名 |

## 运行

确保 ReidFeature 服务已启动（默认 `http://localhost:9000`），然后：

```cmd
dotnet run --project FamilyDiscern
```

## MCP Server 使用

FamilyDiscern 启动时会在后台同进程启动 MCP Server（stdio 传输模式），暴露以下工具：

### 工具列表

#### enroll_member

注册家庭成员。

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| mp4Path | string | 是 | MP4 视频文件绝对路径 |
| memberName | string | 是 | 成员名称 |
| groupId | string | 否 | 组名，为空时使用配置文件第一个组 |

示例调用：
```
enroll_member(mp4Path="D:\\Videos\\dad.mp4", memberName="爸爸")
enroll_member(mp4Path="D:\\Videos\\mom.mp4", memberName="妈妈", groupId="family01")
```

#### recognize_video

识别视频中的人物。

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| mp4Path | string | 是 | MP4 视频文件绝对路径 |
| groupId | string | 否 | 对比组名，为空时使用配置文件第一个组 |

示例调用：
```
recognize_video(mp4Path="D:\\Videos\\unknown.mp4")
recognize_video(mp4Path="D:\\Videos\\clip.mp4", groupId="family01")
```

返回示例：
```
识别结果:
  姓名: 爸爸
  ID: abc123
  组: group1
  总分: 0.7821
  全身ReID: 0.8234
  头肩ReID: 0.7654
  体型: 0.7890
  步态: 0.7456
```

#### list_members

列出指定组的所有成员。

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| groupId | string | 否 | 组名，为空时使用配置文件第一个组 |

#### delete_member

删除指定成员。

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| memberId | string | 是 | 成员 ID |
| groupId | string | 否 | 组名，为空时使用配置文件第一个组 |

### MCP 配置

在 Kiro/IDE 的 MCP 配置中添加：

```json
{
  "mcpServers": {
    "family-discern": {
      "command": "dotnet",
      "args": ["run", "--project", "G:\\Github\\Jiulang\\ReidProj\\FamilyDiscern"],
      "disabled": false
    }
  }
}
```

或者使用编译后的 exe：

```json
{
  "mcpServers": {
    "family-discern": {
      "command": "G:\\Github\\Jiulang\\ReidProj\\FamilyDiscern\\bin\\Debug\\net10.0\\FamilyDiscern.exe",
      "disabled": false
    }
  }
}
```

## 项目结构

```
FamilyDiscern/
├── Models/
│   ├── AppSettings.cs        # 配置模型（读写 appsettings.json）
│   ├── ApiModels.cs          # API 响应模型
│   └── LocalMemberStore.cs   # 本地注册记录（members.json）
├── Services/
│   ├── FfmpegService.cs      # 编码探测 + MP4 转裸流
│   └── ReidClient.cs         # HTTP 客户端（enroll/recognize/list/delete）
├── Mcp/
│   └── FamilyDiscernTools.cs # MCP 工具（4 个 tool）
├── ViewModels/
│   └── MainViewModel.cs      # MVVM 主逻辑
├── Views/
│   ├── MainWindow.axaml      # UI 布局（Tab: 成员管理/视频识别/配置）
│   └── MainWindow.axaml.cs   # 文件选择事件
├── Program.cs                # 启动入口（MCP 后台 + Avalonia GUI）
├── appsettings.json          # 默认配置
└── members.json              # 本地注册记录（运行时生成）
```
