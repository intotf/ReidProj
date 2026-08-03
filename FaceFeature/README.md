# FaceFeature — 门铃摄像头人脸识别服务器

基于 **.NET 10** 与 **ONNX Runtime** 的人脸检测 / 识别服务器实现方案，面向门铃摄像头场景：接收图片或 H264/H265 视频裸流，实时检测人脸并与指定分组中的已知人物进行 1:N 比对识别。

## 特性

- 🔍 **人脸检测**：SCRFD-10g（`det_10g.onnx`），640×640 letterbox 推理，自动按输出张量数量适配模型布局，NMS 去重
- 👤 **人脸特征提取**：ArcFace w600k_r50（`w600k_r50.onnx`，InsightFace buffalo_l 模型包），输出 512 维 L2 归一化特征向量
- 🎯 **最佳人脸策略**：性能优先，仅对面积最大的单人脸做特征提取与比对
- 📹 **视频裸流支持**：通过自带 ffmpeg pipe 流式解码 H264 / H265 裸流（Annex B），文件不落地，支持按帧间隔抽帧
- 🏷️ **分组识别**：`datas/facegroups/{groupId}/{人名}/` 目录即注册表，启动时自动提取特征入库，实现 1:N 识别
- 📡 **两种输入方式**：原始图片二进制上传 或 图片 URL 下载
- ⚡ **性能优化**：`ArrayPool` 缓冲复用、ONNX 会话可独立配置线程数、AOT 发布（`PublishAot`）、源生成 JSON 序列化
- 🐛 **调试辅助**：Debug 构建下自动将带红色人脸框的标注图写入 `out/` 目录

## 技术栈

| 组件 | 说明 |
| --- | --- |
| ASP.NET Core Minimal API | .NET 10，`WebApplication.CreateSlimBuilder` |
| Microsoft.ML.OnnxRuntime 1.27.1 | ONNX 推理 |
| SCRFD-10g | 人脸检测模型 |
| ArcFace w600k_r50 | 人脸特征提取模型 |
| SixLabors.ImageSharp 4.x | 图像解码 / 裁剪 / 缩放 |
| ffmpeg | H264/H265 裸流 → BMP 帧流式解码 |
| System.Numerics.Tensors | 512 维余弦相似度计算 |

## 目录结构

```
FaceFeature/
├── Program.cs                    # 入口：服务注册、路由端点
├── OnnxSessionOptions.cs         # ONNX 会话配置模型（Face / FaceRec）
├── appsettings.json              # Kestrel 端口、ONNX 线程配置
├── Handlers/
│   ├── DetectHandler.cs          # 检测端点处理（图片 / URL / H264 / H265 流）
│   └── RecognizeHandler.cs       # 识别端点处理（1:N 分组比对）
├── Services/
│   ├── FaceDetector.cs           # SCRFD-10g 人脸检测
│   ├── FaceExtractor.cs          # ArcFace 特征提取
│   ├── DetectService.cs          # 检测编排：检测 → 特征提取
│   ├── IFaceGroupProvider.cs     # 人脸分组提供者接口
│   └── MockFaceGroupProvider.cs  # 基于文件目录的实现
├── Payloads/                     # 请求 / 响应模型（record）
├── Helpers/
│   ├── VideoDecoder.cs           # ffmpeg pipe 流式解码
│   └── Log.cs                    # 结构化日志
├── models/                       # ONNX 模型（见“模型准备”）
├── tools/                        # ffmpeg / ffmpeg.exe
├── datas/facegroups/             # 分组人脸注册目录
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
- `models/w600k_r50.onnx` — ArcFace 特征提取

也可以手动放置模型到 `models/` 目录。

### 2. 放置 ffmpeg

将 ffmpeg 可执行文件放入 `tools/` 目录（仅视频流端点需要；图片端点不需要）：

| 平台 | 路径 |
| --- | --- |
| Windows | `tools/ffmpeg.exe` |
| Linux | `tools/ffmpeg` |

### 3. 注册人脸分组

按以下目录结构放入人物照片，目录名即 `groupId` / 人物名称：

```
datas/facegroups/
└── group1/                # 分组 ID
    ├── 张小姐/
    │   ├── 正面.jpeg
    │   └── 侧面.png
    └── 赖弟弟/
        └── 02_0004.jpg
```

服务启动时会对每张照片自动执行人脸检测 + 特征提取，用于后续比对。

### 4. 运行

```bash
cd FaceFeature
dotnet run
```

默认监听 `http://*:9000`（见 `appsettings.json`）。开发模式下受 `launchSettings.json` 影响，默认端口为 `https://localhost:63937`。

> **注意**：内置的 `MockFaceGroupProvider` 是目录实现的参考实现，仅演示用。实际接入时实现 `IFaceGroupProvider` 接口（如从数据库 / Redis 加载人物特征）并在 `Program.cs` 中替换注册即可。

## API

### 健康检查

`GET /`

返回 `HealthCheck`。

### 人脸检测

| 方法 | 路径 | 请求体 | 说明 |
| --- | --- | --- | --- |
| POST | `/detect/image` | `application/octet-stream` 原始图片二进制 | 检测面积最大的最佳人脸 |
| POST | `/detect/imageurl` | JSON `{ "imageUrl": "https://..." }` | 通过 URL 下载图片后检测 |
| POST | `/detect/h264stream` | H264 裸流（Annex B） | 边解码边检测，默认每 5 秒抽一帧 |
| POST | `/detect/h265stream` | H265/HEVC 裸流 | 同上 |

视频流端点支持查询参数：

- `frameIntervalSeconds`：帧间隔秒数（如 `0.5` 表示每 0.5 秒一帧，`<=0` 表示全部帧）

检测响应（无人脸时返回空 / `null`）：

```json
{
  "bbox": { "x": 120, "y": 80, "width": 180, "height": 220 },
  "confidence": 0.98,
  "features": "<base64 编码的 512 维特征向量原始字节>"
}
```

### 人脸识别（1:N 分组比对）

| 方法 | 路径 | 请求体 | 说明 |
| --- | --- | --- | --- |
| POST | `/recognize/image/{groupId}` | `application/octet-stream` | 上传图片识别 |
| POST | `/recognize/imageurl/{groupId}` | JSON | URL 图片识别 |
| POST | `/recognize/h264stream/{groupId}` | H264 裸流 | 流式识别，逐帧返回命中结果 |
| POST | `/recognize/h265stream/{groupId}` | H265/HEVC 裸流 | 同上 |

支持查询参数：

- `similarityThreshold`：相似度阈值（默认 `0.5`，`FaceRecognition.SimilarityThreshold` 常量）
- `frameIntervalSeconds`：视频流抽帧间隔（默认 5 秒）

识别响应：与分组中某人物余弦相似度超过阈值时返回命中人物，否则为空：

```json
{
  "id": "正面.jpeg",
  "groupId": "group1",
  "name": "张小姐",
  "faceSimilarity": 0.9234
}
```

### 调用示例

```bash
# 图片检测
curl -X POST http://localhost:9000/detect/image \
     --data-binary @photo.jpg \
     -H "Content-Type: application/octet-stream"

# 图片识别（group1 分组）
curl -X POST http://localhost:9000/recognize/image/group1 \
     --data-binary @photo.jpg \
     -H "Content-Type: application/octet-stream"

# URL 识别
curl -X POST http://localhost:9000/recognize/imageurl/group1 \
     -H "Content-Type: application/json" \
     -d '{"imageUrl": "https://example.com/face.jpg"}'

# H264 裸流识别（每 5 秒抽一帧）
curl -X POST "http://localhost:9000/recognize/h264stream/group1?frameIntervalSeconds=5" \
     --data-binary @stream.h264 \
     -H "Content-Type: application/octet-stream"
```

### Swagger UI

启动后访问 `http://localhost:9000` 下的 OpenAPI 文档端点（`/openapi/v1.json`），Swagger UI 配置在 `/swagger`（需手动启用）。

## 核心实现说明

### 人脸检测（SCRFD-10g）

- 图像居中 letterbox 缩放至 640×640，黑色填充
- 按输出张量数量自动识别模型布局（3 层 / 5 层、是否含关键点、每像素锚点数）
- 边界框解码后映射回原图坐标，NMS 去重
- 过滤过小的人脸（宽高低于 `MinFaceSize` 的特征不可靠）

### 特征提取（ArcFace）

- 人脸框外扩 20% 获取头部轮廓上下文
- 保持宽高比缩放至 112×112，黑色填充居中
- InsightFace 归一化：`(pixel - 127.5) / 128.0`
- 输出 512 维 L2 归一化特征（`byte[]`，内部为 `float` 原始字节）

### 特征比对

- `FacePerson.Similarity`：基于 `System.Numerics.Tensors.TensorPrimitives` 计算余弦相似度
- 识别时遍历分组内所有人，取超过阈值中的最高相似度者

### 视频流解码（VideoDecoder）

- 启动 ffmpeg 子进程：`-f h264|hevc -i pipe:0 -f image2pipe -c:v bmp pipe:1`
- 上传的裸流经 stdin pipe 写入，stdout 流式读取 BMP 帧（54 字节头 + 像素数据），逐帧解码检测
- 通过 `-r` 参数按帧间隔抽帧，降低检测开销

## 配置

`appsettings.json`：

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `Kestrel.Endpoints.Http.Url` | `http://*:9000` | 监听地址 |
| `Onnx.Face.IntraOpNumThreads` | `1` | 检测会话线程数 |
| `Onnx.FaceRec.IntraOpNumThreads` | `0` | 特征提取会话线程数（0 = 全部核心） |
| 请求体上限 | 20 MB | `Program.cs` Kestrel 限制 |

ONNX 会话级选项（`OnnxSessionOptions`）可独立配置两个模型的 `IntraOpNumThreads`、`InterOpNumThreads`、`ExecutionMode`、`GraphOptimizationLevel`。

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

**Q：识别一直返回空？**
A：检查 `datas/facegroups/{groupId}/` 目录是否存在且含可识别的人脸照片；确认 `similarityThreshold` 阈值是否过高。

**Q：请求体过大被拒绝？**
A：默认上限 20 MB，可在 `Program.cs` 中调整 `MaxRequestBodySize`。

## 路线图 / 待办

- [ ] 将 `MockFaceGroupProvider` 替换为数据库 / Redis 存储实现
- [ ] 人脸注册 / 更新 / 删除的完整 CRUD API
- [ ] 门铃场景触发式识别（事件驱动、唤醒帧）支持
- [ ] 更细粒度的性能指标与耗时监控
