---
name: reIdMp4-cli
description: 使用 ReIdMp4Cli 工具对 MP4 视频按秒抽帧，通过 ReidFeature 识别服务进行人物匹配
author: CherryClaw
trigger: "用户请求对 MP4 视频进行人物识别/查找/匹配，或提及需要分析视频中的人物、使用 Reid 识别视频、按帧搜索视频人物等场景。常见表述：'帮我识别这个视频'、'看看这个视频里有没有某人'、'视频找人'、'mp4 识别'、'从视频中找'"
---

# reIdMp4-cli 视频人物识别工具

## 核心原则

**用户必须提供完整的 MP4 视频绝对路径。** 分组 ID 可选，默认为 `group2`。如果用户未提供视频路径，必须明确询问。

## 工作流程

### 第一步：获取必要参数

必须从用户处获取以下参数：

1. **MP4 视频文件路径** — 待识别的视频文件绝对路径（如 `D:\videos\2026-07\clip.mp4`）
2. **分组 ID**（可选，默认 `group2`） — 服务端预定义的人物分组标识（如 `family01`、`group2`）

如果用户未提供视频路径，按以下方式询问：

> 请提供 MP4 视频文件的绝对路径

如果用户未提供分组 ID，直接使用默认值 `group2`，无需询问。

**禁止在用户未提供视频路径时自行假设或使用占位路径执行命令。**

### 第二步：执行识别

视频路径确认后，执行命令：

```bash
G:\Tools\ReIdMp4Cli\ReIdMp4Cli "<MP4视频绝对路径>"
```

如需指定分组 ID（非默认 `group2`），追加第二个参数：

```bash
G:\Tools\ReIdMp4Cli\ReIdMp4Cli "<MP4视频绝对路径>" "<分组ID>"
```

**可选参数（按需追加）：**

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--server-url` | `http://localhost:9000` | ReidFeature 服务地址 |
| `--threshold` | `0.9` | 相似度阈值 |
| `--flags` | `0` | 检测标志: 0=All, 1=SkipFaceDetection, 2=StopOnFirstFrameHit |
| `--ffmpeg-path` | 自动从 PATH 查找 | ffmpeg 可执行文件路径 |

完整示例：

```bash
G:\Tools\ReIdMp4Cli\ReIdMp4Cli "D:\videos\clip.mp4"
G:\Tools\ReIdMp4Cli\ReIdMp4Cli "D:\videos\clip.mp4" family01 --server-url http://192.168.1.100:5000 --threshold 0.85 --flags 1
```

### 第三步：返回结果

向用户汇报执行结果，指出哪些帧匹配到了人物、最高相似度、来源图片等信息。

## 参数说明

- **MP4 视频文件路径** — 仅支持单个 MP4 文件，不支持目录批量处理
- **分组 ID**（可选，默认 `group2`） — 对应 ReidFeature 服务端 `PersonGroupProvider` 中注册的分组名称

## 输出说明

- 每帧输出匹配到的人物及其相似度
- 汇总输出总帧数、匹配帧数、匹配率
- 展示最高相似度的匹配详情（帧名、人物、相似度、来源图片名）
- 匹配到人物时程序退出码为 0，否则为 1

## 行为规则

1. **MP4 视频路径必须由用户提供** — 不要猜测、不要使用默认值、不要用相对路径；分组 ID 可省略，默认为 `group2`
2. 自动通过 ffmpeg 按 **1 帧/秒** 的频率抽帧
3. 每帧发送到 ReidFeature 服务的 `/recognize/image/{groupId}` 接口进行识别
4. 服务端返回的每个匹配结果均会展示（可能一帧匹配多个人物）
5. 临时帧文件在程序结束后自动清理
6. 程序退出码：0 表示有匹配，1 表示参数错误或无匹配
7. 如果执行失败，向用户反馈错误信息，确认路径和服务地址是否正确
