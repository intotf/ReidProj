# ReidFeature — 视频流人物重识别服务

基于 **ONNX Runtime + ImageSharp + 自研 ByteTrack** 的 .NET 10 人物重识别（Person Re-Identification）服务。
接收 H264 / H265 视频裸流，实时完成 **人物检测 → 多目标跟踪 → 四维特征融合 → 检索匹配**，面向家庭监控、单摄像头场景。

## 核心能力

- **YOLOv11n 人物检测**：整帧推理，输出人物边界框与置信度
- **ByteTrack 多目标跟踪**：纯 C# 实现的 8 维卡尔曼滤波 + 匈牙利线性分配 + IoU 级联匹配
- **四维特征融合**（换衣鲁棒）：
  | 维度 | 来源 | 默认权重 |
  |---|---|---|
  | 全身 ReID | FastReID ResNet50-IBN-a（2048-d） | 0.20 |
  | 头肩 ReID | 同一模型，仅取 bbox 上半 38% 区域 | 0.30 |
  | 体型标量 | MoveNet Lightning 关键点（头身比 / 肩髋比） | 0.30 |
  | 步态标量 | ByteTrack 轨迹中心点（步频 / 水平摆幅） | 0.20 |
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
│   ├── YoloDetector.cs         #   YOLOv11n 人物检测 + NMS
│   ├── ByteTrackTracker.cs     #   ByteTrack 多目标跟踪器（纯 C#）
│   ├── ReIdExtractor.cs        #   FastReID 特征提取（全身 / 头肩）
│   ├── PoseEstimator.cs        #   MoveNet 姿态估计 → 体型标量
│   ├── TrackFusionService.cs   #   Track 内四维特征融合
│   ├── DetectService.cs        #   检测编排：解码 → 检测 → 跟踪 → 缓存
│   ├── FamilyGalleryService.cs #   家庭成员 Gallery（持久化 + datas/family 导入）
│   └── IPersonGroupProvider.cs #   家庭成员提供者接口
├── Helpers/
│   ├── VideoDecoder.cs         # ffmpeg 管道流式解码
│   └── BoundingBoxHelper.cs    # bbox Clamp 工具
├── Payloads/                   # 请求/响应模型
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

**`POST /detect/h264stream`** · **`POST /detect/h265stream`**

对视频流逐帧执行「检测 → 跟踪 → 融合」，返回每个完成 Track 的四维特征包。

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

**`POST /recognize/h264stream/{groupId}`** · **`POST /recognize/h265stream/{groupId}`**

在检测/跟踪/融合之上，将每个 Track 的特征包与 `{groupId}` 下的 Gallery 成员逐维匹配，
返回**单个最佳匹配**。判定条件：

- 最高分 > 0.62（`HitThreshold`）
- 且与同 Track 次高分差 > 0.08（`MarginThreshold`）

Query 参数：

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `frameIntervalSeconds` | double | 0.5 | 帧间隔（秒） |
| `wCloth` / `wHead` / `wBody` / `wGait` | float | 0.20 / 0.30 / 0.30 / 0.20 | 四维权重，可临时调整 |

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
| POST | `/family/enroll/h264/{groupId}/{memberName}` | 上传视频流注册成员 | `EnrollResult` |
| POST | `/family/enroll/h265/{groupId}/{memberName}` | 同上（H265） | `EnrollResult` |
| DELETE | `/family/{groupId}/{memberId}` | 删除成员 | `200` / `404` |
| GET | `/family/{groupId}` | 列出成员摘要 | `MemberInfo[]` |

`EnrollResult`：`{ "memberId": "...", "name": "...", "groupId": "..." }`
`MemberInfo`：`{ "id": "...", "name": "...", "enrolledAt": "..." }`

### 健康检查

**`GET /`** → `HealthCheck`

## 数据目录（datas/）

| 目录 | 用途 |
|---|---|
| `datas/gallery/` | Gallery 持久化，每个分组一个 `{groupId}.json`（成员四维特征包） |
| `datas/family/` | 启动时自动导入：每个子目录 `datas/family/{成员名}/enroll.{h264,h265,mp4}` 会被检测、融合并注册到 `default` 分组 |

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
