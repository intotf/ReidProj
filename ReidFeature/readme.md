# ReidFeature — 视频流人物重识别服务

基于 **ONNX Runtime + ImageSharp + 自研 ByteTrack** 的 .NET 10 人物重识别（Person Re-Identification）服务。
接收 H264 / H265 视频裸流，实时完成 **人物检测 → 多目标跟踪 → 四维特征融合 → 检索匹配**，面向家庭监控、单摄像头场景。

## 核心能力

- **YOLOv11n 人物检测**：整帧推理，输出人物边界框与置信度
- **ByteTrack 多目标跟踪**：纯 C# 实现的 8 维卡尔曼滤波 + 匈牙利线性分配 + IoU 级联匹配
- **四维特征融合**（换衣鲁棒）：
  | 维度 | 来源 | 默认权重 |
  |---|---|---|
  | 全身 ReID | FastReID ResNet50-IBN-a（2048-d） | 0.30 |
  | 头肩 ReID | 同一模型，仅取 bbox 上半 38% 区域 | 0.40 |
  | 体型标量 | MoveNet Lightning 关键点（头身比 / 肩髋比） | 0.20 |
  | 步态标量 | ByteTrack 轨迹中心点（步频 / 水平摆幅） | 0.10 |
- **质量加权融合**：按 bbox 面积 × 检测置信度选取 Top-K 帧（K ≤ 5）并行特征提取后加权平均
- **家庭成员 Gallery**：注册 / 删除 / 列出 / JSON 持久化，识别时与 Gallery 成员逐维匹配
- **Native AOT 发布**：零运行时依赖的独立可执行文件

## 技术栈

- .NET 10 / ASP.NET Core Minimal API
- Microsoft.ML.OnnxRuntime 1.27
- SixLabors.ImageSharp（图像处理）
- Swashbuckle / Microsoft.AspNetCore.OpenApi（OpenAPI 文档）
- 内置 ffmpeg（`tools/`，流式解码视频，不落地文件）

## 目录结构

```
ReidFeature/
├── Program.cs                  # 入口：服务注册、路由端点
├── OnnxSessionOptions.cs       # ONNX 会话配置（可绑定 appsettings.json）
├── AppJsonSerializerContext.cs # AOT JSON 序列化上下文
├── Handlers/                   # HTTP 端点处理器
│   ├── DetectHandler.cs        #   检测端点
│   ├── RecognizeHandler.cs     #   识别端点
│   └── EnrollmentHandler.cs    #   家庭成员注册端点
├── Services/                   # 核心服务
│   ├── YoloDetector.cs         #   YOLOv11n 人物检测 + NMS（Singleton）
│   ├── ByteTrackTracker.cs     #   ByteTrack 多目标跟踪器（纯 C#，Scoped）
│   ├── ReIdExtractor.cs        #   FastReID 特征提取（全身 / 头肩，Singleton）
│   ├── PoseEstimator.cs        #   MoveNet 姿态估计 → 体型标量（Singleton）
│   ├── TrackFusionService.cs   #   Track 内四维特征融合（Scoped）
│   ├── DetectService.cs        #   检测编排：解码 → 检测 → 跟踪 → 缓存（Scoped）
│   ├── FamilyGalleryService.cs #   家庭成员 Gallery（持久化，Singleton）
│   └── IFamilyMemberProvider.cs #  家庭成员提供者接口
├── Helpers/
│   ├── VideoDecoder.cs         # ffmpeg 管道流式解码
│   ├── BoundingBoxHelper.cs    # bbox Clamp 工具
│   └── HungarianSolver.cs      # 匈牙利算法求解器（跟踪器内部使用）
├── Payloads/                   # 请求/响应模型、枚举与数据模型（含 CropType / GalleryData / GalleryEntry）
├── models/                     # ONNX 模型（见下）
├── tools/                      # ffmpeg 二进制
├── datas/                      # 运行时数据目录（见下）
├── scripts/
│   ├── setup_models.py         # 模型下载 / ONNX 导出脚本
│   └── fast-reid/              # FastReID 参考实现（仅用于导出模型）
└── appsettings.json            # 配置
```

## 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Python 3.10+（仅首次准备模型时需要）
- 模型文件（见下）

### 模型准备

服务启动时从 `models/` 目录加载 3 个 ONNX 模型：

| 文件 | 用途 | 输入 |
|---|---|---|
| `yolo11n.onnx` | 人物检测 | 640×640 RGB |
| `reid_model.onnx` | 全身 / 头肩 ReID 特征 | 128×256 RGB |
| `movenet_lightning.onnx` | 姿态估计 | 192×192 RGB |

一键下载并导出：

```bash
cd ReidProj
python ReidFeature/scripts/setup_models.py
```

> 说明：`models/scrfd_10g.onnx`、`w600k_r50.onnx`、`yolo11n-face.onnx` 属于同仓库的 **FaceFeature**（人脸识别）项目，ReidFeature 不使用。

### ffmpeg

视频解码依赖 `tools/` 下的 ffmpeg 二进制：

- Windows：`tools/ffmpeg.exe`
- Linux：`tools/ffmpeg`（需可执行权限）

项目文件已配置按目标平台自动复制到输出目录。

## 快速开始

```bash
cd ReidProj/ReidFeature
dotnet restore
dotnet run
```

默认监听：

- 开发环境（`launchSettings.json`）：`http://localhost:5102`，浏览器打开 `http://localhost:5102/swagger` 查看 API 文档
- 生产配置（`appsettings.json`）：`http://*:9000`

### 配置（appsettings.json）

```jsonc
{
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://*:9000" } } }, // 监听地址
  "Onnx": {
    "Yolo": { "IntraOpNumThreads": 1, "InterOpNumThreads": 1 },
    "ReId": { "IntraOpNumThreads": 0 },  // 0 = 使用全部核心
    "Pose": { "IntraOpNumThreads": 1 }
  }
}
```

各模型可独立配置线程数与执行模式（在 `OnnxSessionOptions` 中定义）。

## API 文档

所有视频端点请求体均为 **原始视频裸流字节**（`application/octet-stream`），最大请求体 20 MB。

### 检测（Detect）

**`POST /detect/stream`**

对视频流逐帧执行「检测 → 跟踪 → 融合」，返回每个完成 Track 的四维特征包。
H264 / H265 裸流均可，编码由服务端自动识别。

Query 参数：

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `frameIntervalSeconds` | double | 0.5 | 帧间隔（秒）。每隔 N 秒解码一帧；`≤ 0` 时解码全部帧 |

响应：`PersonDetection[]`

```json
[
  {
    "bbox": { "x": 112, "y": 84, "width": 96, "height": 268 },
    "confidence": 1.0,
    "features": "base64...",
    "featurePack": {
      "vecCloth": "base64...",
      "vecHead": "base64...",
      "bodySignals": [1.82, 1.36],
      "gaitSignals": [0.84, 6.12]
    },
    "trackId": 3
  }
]
```

### 识别（Recognize）

**`POST /recognize/{groupId}`**

在检测/跟踪/融合之上，将每个 Track 的特征包与 `{groupId}` 下的 Gallery 成员逐维匹配，
返回**单个最佳匹配**。H264 / H265 裸流均可，编码由服务端自动识别。判定条件（多成员库混合逻辑）：

- 无歧义：最高分 > 0.88（`HitThreshold`）且与次高分差 > 0.08（`MarginThreshold`）→ 命中
- 有歧义（margin 不满足）：最高分 ≥ 0.965（`HighConfidenceThreshold`）→ 仍命中（多为同一人多条目/相似成员）
- 其余情况 → `stranger`

Query 参数：

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `frameIntervalSeconds` | double | 0.5 | 帧间隔（秒） |
| `wCloth` / `wHead` / `wBody` / `wGait` | float | 0.30 / 0.40 / 0.20 / 0.10 | 四维权重；门铃场景实测建议 **0.50 / 0.50 / 0 / 0**（去掉体型/步态） |
| `highConfidenceThreshold` | float | 0.965 | 高分兜底阈值（歧义时仍命中），取值 [0,1]，可动态调整 |

响应：`PersonRecognition`（未命中时返回 `name = "stranger"`，`id` 为空串）

```json
{
  "id": "a1b2c3d4e5f6",
  "groupId": "family1",
  "name": "张三",
  "score": 0.873,
  "clothScore": 0.91,
  "headScore": 0.88,
  "bodyScore": 0.84,
  "gaitScore": 0.79
}
```

### 家庭成员管理（Family）

| 方法 | 路径 | 说明 | 响应 |
|---|---|---|---|
| POST | `/family/enroll/{groupId}/{memberName}` | 上传视频流注册成员（H264/H265 自动识别）；`?append=true` 时始终新增一条记录 | `EnrollResult` |
| POST | `/family/enroll-batch/{groupId}/{memberName}` | 同一人多段注册：multipart 一次上传多段视频；`append=false`（默认）各段等权融合为一条，`append=true` 时每段视频各自独立成一条成员记录（多成员库模式） | `EnrollBatchResult` |
| POST | `/family/merge/{groupId}` | 成员合并去重：把同一人的多条成员特征等权融合为一条，删除被合并成员（JSON 请求体） | `MemberInfo[]` |
| DELETE | `/family/{groupId}/{memberId}` | 删除成员 | `200` / `404` |
| GET | `/family/{groupId}` | 列出成员摘要 | `MemberInfo[]` |

`EnrollResult`：`{ "memberId": "...", "name": "...", "groupId": "..." }`
`MemberInfo`：`{ "id": "...", "name": "...", "enrolledAt": "..." }`
`EnrollBatchResult`：`{ "memberId": "...", "name": "...", "groupId": "...", "segmentCount": 2, "segments": [{ "fileName": "...", "trackId": 1 }] }`

#### 多成员库注册流程（推荐）

**多成员模式**：同一成员注册多段视频时用 `append=true`，每段视频独立成一条成员记录，
配合识别的"高分兜底"逻辑（margin 歧义时 ≥0.965 仍命中），同一人多条目不会被误拒。

```powershell
# 方式一：一键脚本（自动 mp4→裸流；FamilyDiscern/脚本默认 append=true，每段独立成条）
.\scripts\enroll_member_batch.ps1 -Folder D:\clips\laiguowei -GroupId group1 -MemberName 赖国伟 -Port 9000

# 方式二：curl multipart（视频需先转成 Annex-B 裸流）
curl.exe -X POST "http://localhost:9000/family/enroll-batch/group1/%E8%B5%96%E5%9B%BD%E4%BC%9F?frameIntervalSeconds=0.5&append=true" `
  -F "videos=@seg1.h264" -F "videos=@seg2.h264" -F "videos=@seg3.h264"
```

如需把多条记录合并回一条，用下面的 `merge` 接口。

成员合并去重（例如把"赖国伟-背面/赖国伟-正面"两条合并成一条）：

```powershell
# 先 GET /family/{groupId} 拿到成员 ID，再：
.\scripts\merge_members.ps1 -GroupId group1 -TargetMemberId 9f51332364ad -MergeMemberIds 947b55f15b23 -Port 9000

# 等价 curl：
curl.exe -X POST "http://localhost:9000/family/merge/group1" `
  -H "Content-Type: application/json" `
  --data '{"targetMemberId":"9f51332364ad","mergeMemberIds":["947b55f15b23"]}'
```

> 注意：`enroll-batch` 会一次上传多段视频，服务端 `MaxRequestBodySize` 已放宽到 100MB；
> 单视频注册仍建议用 `/family/enroll/...`（20MB 内的单个裸流）。

> **注册源策略**：成员注册应使用固定的注册源视频（如 `G:\Tools\MediaDownloader\downloads\laiguowei\目标` 下的两个视频），
> 不要用其他视频注册，以保证库项特征一致、识别结果稳定。

### 健康检查

**`GET /`** → `HealthCheck`

## 数据目录（datas/）

| 目录 | 用途 |
|---|---|
| `datas/gallery/` | Gallery 持久化，每个分组一个 `{groupId}.json`（成员四维特征包） |
| `datas/family/` | 预留目录，注册请通过 `/family/enroll/...` 接口上传视频流 |

## 构建与发布

普通构建：

```bash
dotnet build
```

Native AOT 发布（独立可执行文件，无需 .NET 运行时）：

```bash
dotnet publish -c Release -r win-x64 /p:PublishAot=true
```

发布产物包含 `models/*.onnx`、`tools/ffmpeg*` 与 `datas/` 目录。

## 工作流程（一次请求的处理链路）

```
视频裸流
  │  VideoDecoder（ffmpeg 管道，按 frameIntervalSeconds 抽帧）
  ▼
YoloDetector（人物检测 + NMS）
  ▼
ByteTrackTracker（卡尔曼预测 → 高分关联 → 低分关联 → 轨迹管理）
  ▼
TrackFusionService（Top-K 质量加权：全身 ReID + 头肩 ReID + 体型 + 步态）
  ├─ Detect 端点 → 直接返回 PersonDetection[]
  └─ Recognize 端点 → 与 Gallery 成员四维余弦匹配 → 最佳结果
```

## 注意事项

- `DetectService`、`ByteTrackTracker` 以 **Scoped** 注册：每个 HTTP 请求独立状态，`ByteTrackTracker` 需通过 `Reset()` 或作用域重建以隔离不同请求
- 请求体超过 20 MB 会被 Kestrel 拒绝（可在 `Program.cs` 调整 `MaxRequestBodySize`）
- ReID / 姿态模型均从模型元数据动态读取输入输出名，不同来源的 ONNX 兼容性较好
- 视频抽帧场景下（默认 0.5s/帧）人物帧间位移较大，ByteTrack 的激活阈值 `MinHitStreak=3` 已做针对性权衡
