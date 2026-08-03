# FaceFeature — 门铃摄像头人脸识别服务器

基于 **.NET 10** 与 **ONNX Runtime** 的人脸识别服务器，面向门铃摄像头场景：接收 H264 / H265 视频裸流，对整段视频做**多帧特征融合**后与指定分组中的已知人物进行 1:N 比对，一个视频输入只产出一个识别结果。

## 特性

- 🔍 **人脸检测**：SCRFD-10g（`det_10g.onnx`），640×640 letterbox 推理，固定模型布局（3 层 FPN、5 关键点），NMS 去重
- 🎯 **五点对齐**：利用 SCRFD 关键点做 InsightFace 标准相似变换对齐到 112×112，替代简单裁剪缩放，显著提升同人相似度
- 🔆 **清晰帧筛选**：对齐后人脸 Laplacian 方差作为清晰度分数，低于阈值的模糊帧直接跳过
- 🧬 **视频多帧融合**：增量累加整段流的特征（`float[]`），融合向量连续收敛或达到帧数上限时**提前完成**，无需等整段视频结束
- 🏷️ **人脸管理**：注册 / 查询 / 删除 REST API，特征与元数据持久化为 JSON 索引（`index.json`），重启免重新提取
- ⚡ **性能优化**：`ArrayPool` 大缓冲复用、`TensorPrimitives` SIMD 向量化、ONNX 会话独立线程配置、AOT 发布、源生成 JSON 序列化
- 🔌 **线格式**：内部统一 `float[]` 特征，仅在 HTTP / JSON 边界经自定义 Converter 以 base64 编解码

## 技术栈

| 组件 | 说明 |
| --- | --- |
| ASP.NET Core Minimal API | .NET 10，`WebApplication.CreateSlimBuilder` |
| Microsoft.ML.OnnxRuntime 1.27.1 | ONNX 推理 |
| SCRFD-10g | 人脸检测模型（`det_10g.onnx`） |
| ArcFace R100（glint360k） | 人脸特征提取模型（`glintr100.onnx`，可配置切换） |
| SixLabors.ImageSharp 4.x | 图像解码 / 对齐 / 缩放 |
| ffmpeg | H264/H265 裸流 → BMP 帧流式解码 |
| System.Numerics.Tensors | 特征融合与余弦相似度计算 |

## 目录结构

```
FaceFeature/
├── Program.cs                    # 入口：服务注册、路由端点
├── OnnxSessionOptions.cs         # ONNX 会话配置模型（Face / FaceRec）
├── FaceQualityOptions.cs         # 清晰度筛选配置模型
├── AppJsonSerializerContext.cs   # 源生成 JSON 序列化上下文
├── appsettings.json              # Kestrel 端口、ONNX 线程、清晰度阈值配置
├── Handlers/                     # HTTP 处理器（薄编排，不包含复杂逻辑）
│   ├── DetectHandler.cs          # 视频检测端点（融合）
│   ├── RecognizeHandler.cs       # 视频识别端点（融合 + 1:N 比对）
│   └── FaceGroupHandler.cs       # 人脸管理端点
├── Services/                     # 需 DI 注册的服务
│   ├── FaceDetector.cs           # SCRFD-10g 人脸检测
│   ├── FaceExtractor.cs          # 对齐 + ArcFace 特征提取 + 清晰度评估
│   ├── DetectService.cs          # 检测编排 + 视频逐帧检测流
│   └── FaceGroupService.cs       # 人脸分组管理（注册 / 查询 / 删除 / 持久化）
├── Payloads/                     # 数据模型（API 线格式使用 public，其余 internal）
├── Helpers/                      # 静态工具
│   ├── VideoDecoder.cs           # ffmpeg pipe 流式解码
│   ├── FaceVideoFusion.cs        # 视频多帧融合（收敛提前完成）
│   └── Log.cs                    # 结构化日志
├── models/                       # ONNX 模型（见“模型准备”）
├── tools/                        # ffmpeg / ffmpeg.exe
├── datas/facegroups/             # 人脸注册数据（图片 + index.json）
└── scripts/setup_models.py       # 模型下载脚本
```

## 快速开始

### 环境要求

- .NET 10 SDK
- Python 3（仅用于模型下载脚本）
- ffmpeg（放至 `tools/`，Windows 为 `ffmpeg.exe`）

### 1. 准备模型

```bash
cd FaceFeature
python scripts/setup_models.py
```

脚本自动从 [InsightFace buffalo_l](https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip) 提取并输出：

- `models/det_10g.onnx` — SCRFD-10g 人脸检测
- `models/glintr100.onnx` — ArcFace 特征提取（默认；如需切换旧模型，保留 `models/w600k_r50.onnx` 并在 `Onnx:FaceRecognitionModelName` 中指定文件名）

也可以手动放置模型到 `models/` 目录。

### 2. 放置 ffmpeg

将 ffmpeg 可执行文件放入 `tools/` 目录：

| 平台 | 路径 |
| --- | --- |
| Windows | `tools/ffmpeg.exe` |
| Linux | `tools/ffmpeg` |

### 3. 注册人脸

启动服务后通过人脸管理接口注册（注册照会检测、对齐、提特征并持久化）：

```bash
# 在 group1 分组下注册一张照片，人物名 lai
curl -X POST "http://localhost:9000/faces/group1/register?name=lai" \
     --data-binary @out_0005.png \
     -H "Content-Type: application/octet-stream"
```

注册成功返回 `FaceInfo`（含 `id`、`features`）；未检测到人脸或参数非法时返回 `400 {"error":"..."}`。

### 4. 运行

```bash
cd FaceFeature
dotnet run
```

默认监听 `http://*:9000`（见 `appsettings.json`）。开发模式下受 `launchSettings.json` 影响，默认端口为 `https://localhost:63937`。

## API

### 健康检查

`GET /` → 返回 `HealthCheck`。

### 人脸检测（视频）

| 方法 | 路径 | 请求体 | 说明 |
| --- | --- | --- | --- |
| POST | `/detect/stream` | H264/H265 裸流（Annex B） | 多帧融合后返回单个检测结果（编码由 VideoDecoder 自动识别） |

查询参数：

- `frameIntervalSeconds`：抽帧间隔秒数，默认 `0.5`；`<=0` 表示解码全部帧
- `fusionFrames`：融合帧数上限，默认 `30`；`<=0` 表示不设上限

响应（无人脸时返回 `null`）：

```json
{
  "bbox": { "x": 1248, "y": 639, "width": 123, "height": 160 },
  "confidence": 0.708,
  "features": "<base64 编码的 512 维特征原始字节>",
  "sharpness": 710.3
}
```

### 人脸识别（1:N 分组比对，视频）

| 方法 | 路径 | 请求体 | 说明 |
| --- | --- | --- | --- |
| POST | `/recognize/stream/{groupId}` | H264/H265 裸流（Annex B） | 整段流融合后返回单个识别结果（编码由 VideoDecoder 自动识别） |

查询参数：

- `frameIntervalSeconds`：抽帧间隔秒数，默认 `0.5`
- `similarityThreshold`：相似度阈值，默认 `0.6`（按门禁摄像头实测标定，见“阈值标定”）
- `fusionFrames`：融合帧数上限，默认 `30`

响应：融合后与分组中某人物余弦相似度超过阈值时返回命中人物，否则返回 `null`：

```json
{
  "id": "20260803_195324_656-d812558d",
  "groupId": "group1",
  "name": "lai",
  "faceSimilarity": 0.8651
}
```

### 人脸管理

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/faces/{groupId}/register?name={人物名}` | 注册人脸（原始图片字节），成功返回 `FaceInfo`（含特征） |
| GET | `/faces/{groupId}` | 分组人脸列表（`FaceInfo[]`，不含特征） |
| GET | `/faces/{groupId}/{faceId}` | 单张详情（含特征） |
| DELETE | `/faces/{groupId}/{faceId}` | 删除，返回 `{"deleted":true}`；不存在返回 `404` |

错误统一返回 `{"error":"..."}`（400）；`groupId` / `name` / `faceId` 会做路径穿越防护。

### 调用示例

```bash
# H264/H265 裸流识别（默认参数：0.5s 抽帧 + 多帧融合，编码自动识别）
curl -X POST http://localhost:9000/recognize/stream/group1 \
     --data-binary @stream.h264 \
     -H "Content-Type: application/octet-stream"

# 指定阈值与抽帧间隔
curl -X POST "http://localhost:9000/recognize/stream/group1?similarityThreshold=0.7&frameIntervalSeconds=0.2" \
     --data-binary @stream.h264 \
     -H "Content-Type: application/octet-stream"

# 注册人脸
curl -X POST "http://localhost:9000/faces/group1/register?name=lai" \
     --data-binary @photo.jpg \
     -H "Content-Type: application/octet-stream"

# 查询 / 删除
curl http://localhost:9000/faces/group1
curl -X DELETE http://localhost:9000/faces/group1/{faceId}
```

### Swagger UI

启动后访问 `http://localhost:9000` 下的 OpenAPI 文档端点（`/openapi/v1.json`），Swagger UI 配置在 `/swagger`（需手动启用）。

## 核心实现说明

### 人脸检测（SCRFD-10g）

- 图像居中 letterbox 缩放至 640×640，黑色填充；模型固定为 3 层 FPN、每像素 2 anchor、channels-last、5 关键点
- 边界框与关键点解码后映射回原图坐标，NMS 去重，过滤小于 `MinFaceSize` 的人脸

### 五点对齐（FaceExtractor）

- 用 SCRFD 5 关键点（左眼、右眼、鼻尖、左嘴角、右嘴角）做最小二乘相似变换，仿射到 InsightFace ArcFace 112×112 标准模板
- 逆映射双线性采样，等价 `cv2.warpAffine(INTER_LINEAR)`；无关键点时回退为裁剪缩放

### 清晰帧筛选

- 对齐后人脸做灰度化 → 3×3 Laplacian → 响应方差（`TensorPrimitives` 向量化求和）作为清晰度分数
- 低于 `FaceQuality:SharpnessThreshold`（默认 10）的帧跳过，不参与特征提取与融合

### 视频多帧融合（FaceVideoFusion）

- 逐帧增量累加特征（`TensorPrimitives.Add`），均值 + L2 归一化
- 累计 ≥3 帧后，相邻融合向量余弦连续 2 次 ≥ 0.99（或达到 `fusionFrames` 上限）即判定收敛，**提前完成**并停止读取剩余流
- 一个视频输入只产出一个融合结果

### 特征比对

- `FacePerson.Similarity`：基于 `TensorPrimitives.Dot / Norm` 计算余弦相似度
- 内部链路统一 `float[]`；仅 HTTP 响应与 `index.json` 持久化经 `FloatArrayBase64Converter` 以 base64 编解码

### 人脸管理持久化

- 注册时保存原始图片到 `datas/facegroups/{groupId}/images/{faceId}.jpg`，并把特征与元数据（base64）写入 `{groupId}/index.json`
- 启动时只读取各分组 `index.json` 载入内存，不扫描图片、不重新提取特征

### 视频流解码（VideoDecoder）

- 启动 ffmpeg 子进程：`-f h264|hevc -i pipe:0 -f image2pipe -c:v bmp pipe:1`
- 上传的裸流经 stdin pipe 写入，stdout 流式读取 BMP 帧（54 字节头 + 像素数据），逐帧解码检测
- 通过 `-r` 参数按帧间隔抽帧，降低检测开销

## 配置

`appsettings.json`：

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `Kestrel.Endpoints.Http.Url` | `http://*:9000` | 监听地址 |
| `Onnx.Face.IntraOpNumThreads` | `1` | 检测会话线程数（门禁场景实测建议 2~4，约 2.6 倍提速） |
| `Onnx.FaceRec.IntraOpNumThreads` | `0` | 特征提取会话线程数（0 = 全部核心） |
| `FaceQuality.Enabled` | `true` | 是否启用清晰帧筛选 |
| `FaceQuality.SharpnessThreshold` | `10` | 清晰度阈值（Laplacian 方差，按摄像头画质标定） |
| 请求体上限 | 20 MB | `Program.cs` Kestrel 限制 |

ONNX 会话级选项（`OnnxSessionOptions`）可独立配置两个模型的 `IntraOpNumThreads`、`InterOpNumThreads`、`ExecutionMode`、`GraphOptimizationLevel`。

## 阈值标定

默认相似度阈值 `0.6` 基于门禁摄像头实测数据标定：

- 同人多帧融合约 `0.84+`，单帧最高约 `0.88`
- 同人跨帧单帧约 `0.52~0.68`（受跨尺度 / 姿态影响）

正式上线前请用一批**异人样本**验证误识率：若异人相似度普遍低于 `0.5`，`0.6` 是安全的；否则需上调。清晰度阈值同理按实际摄像头画质调整（可在日志中观察每帧 sharpness 分数）。

## 发布部署

```bash
dotnet publish -c Release -r win-x64
# 或
dotnet publish -c Release -r linux-x64
```

项目已启用 `PublishAot`（原生 AOT），发布产物无需安装 .NET 运行时；ONNX Runtime 原生库与 ffmpeg 会随发布一并复制到输出目录。

> 提示：原生 AOT 下请确保 `datas/`、`models/`、`tools/` 内容随发布目录一并部署。

## 常见问题

**Q：启动报错 "请先将 det_10g.onnx 放到 models/ 目录下"？**
A：未找到模型文件，先执行 `python scripts/setup_models.py` 或将模型放入 `models/`。

**Q：视频流端点报 "找不到 ffmpeg"？**
A：将 ffmpeg 可执行文件放入 `tools/` 目录（Windows: `ffmpeg.exe`，Linux: `ffmpeg`）。

**Q：识别一直返回 null？**
A：确认分组下已通过 `/faces/{groupId}/register` 注册人脸；确认 `similarityThreshold` 是否过高；可用 `/detect/stream` 观察返回的 `sharpness` 判断输入清晰度。

**Q：请求体过大被拒绝？**
A：默认上限 20 MB，可在 `Program.cs` 中调整 `MaxRequestBodySize`。

**Q：特征列表现为什么是 base64？**
A：内部统一使用 `float[]`；HTTP 响应与 `index.json` 为压缩线格式，通过 `FloatArrayBase64Converter` 自动编解码，调用方无需关心。

## 路线图 / 待办

- [ ] 将文件目录持久化替换为数据库 / Redis 存储实现
- [ ] 门铃场景触发式识别（事件驱动、唤醒帧）支持
- [ ] 更细粒度的性能指标与耗时监控
- [ ] 检测模型 INT8 量化与精度回归
