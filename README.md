# ReidProj

## ReidFeature 技术特点

`ReidFeature` 是一个面向行人重识别（Person Re-Identification）的 CPU 推理服务。它接收图片、图片 URL 或 H264/H265 裸流，完成人物检测、人物区域裁剪、ReID 特征提取，并可选返回人物区域内的人脸框。服务只负责生成检测结果与特征，不包含身份库、向量检索或相似度匹配。

### 技术栈

- **.NET 10 / ASP.NET Core Minimal API**：使用 `WebApplication.CreateSlimBuilder` 构建轻量 HTTP 服务。
- **ONNX Runtime 1.27.1**：加载 YOLO、FastReID 和人脸检测 ONNX 模型，在 CPU 上执行推理。
- **ImageSharp 3.1.12**：负责图片解码、缩放、Letterbox、裁剪及张量预处理。
- **Native AOT**：项目默认启用 `PublishAot`，并配合源生成 JSON 序列化，降低冷启动和运行时依赖。
- **OpenAPI / Swagger UI**：运行后可通过 `/openapi/v1.json` 获取接口描述，并使用 Swagger UI 调试。

### 推理流程

```text
图片 / 图片 URL / H264-H265 裸流
  → 图片解码或 FFmpeg 视频抽帧
  → YOLOv11n 人物检测（640×640 Letterbox，置信度过滤 + NMS）
  → 按人物框裁剪
  → 可选的人脸检测
  → FastReID 特征提取（128×256）
  → 返回人物框、置信度、特征和人脸框
```

人物检测只读取 COCO `person` 类别；阈值为 `0.20`，NMS IoU 阈值为 `0.45`。ReID 特征以 `float32` 的原始字节数组返回，在 JSON 中表现为 Base64 字符串，客户端需将其解码为 `float32` 向量后再计算相似度。

### HTTP 接口

| 方法 | 路径 | 输入 | 说明 |
|---|---|---|---|
| GET | `/` | 无 | 健康检查，返回 `HealthCheck` |
| POST | `/detect/image` | `application/octet-stream` | 检测单张图片 |
| POST | `/detect/imageurl` | JSON `{ "imageUrl": "..." }` | 下载并检测远程图片 |
| POST | `/detect/h264stream` | H264 Annex B 裸流 | 按间隔抽帧检测 |
| POST | `/detect/h265stream` | H265/HEVC 裸流 | 按间隔抽帧检测 |

查询参数 `flags` 支持组合：`0` 为全部功能，`1` 跳过人脸检测，`2` 在视频首个命中帧后停止；视频接口还支持 `frameIntervalSeconds`，默认每 5 秒抽取一帧。

### 配置、性能与部署

服务默认监听 `9000` 端口，Kestrel 请求体上限为 20 MiB。`appsettings.json` 可分别配置 YOLO、ReID 和 Face ONNX Session 的 `IntraOpNumThreads`/`InterOpNumThreads`，也可通过 `Onnx__Yolo__IntraOpNumThreads` 等环境变量覆盖。

三个推理器以单例注册，模型在服务启动时加载一次；输入缓冲区使用 `ArrayPool<float>` 复用以减少大对象分配。推理属于 CPU 密集型工作，建议从 2 vCPU、2 GB 内存、单实例并发 2 起步。模型文件会复制到输出目录，FFmpeg 根据目标 RID 从 `tools` 目录复制。项目当前默认 RID 为 `win-x64`，发布 Linux 容器时需要显式指定 Linux RID 并准备对应原生依赖。更多部署信息见 [部署指南](docs/deployment-guide.md)。

### 运行

```cmd
dotnet run --project ReidFeature\ReidFeature.csproj
```

模型必须位于应用目录的 `models` 文件夹中，包括 `yolo11n.onnx`、`reid_model.onnx` 和 `yolo11n-face.onnx`；缺少模型时服务会在启动阶段失败。

### 实现约束与注意事项

- 模型文件名、输入名、输入尺寸和阈值目前写在代码中，替换模型时必须保持 ONNX 输入输出契约一致。
- `/detect/imageurl` 应仅在可信网络环境使用；生产环境建议增加 URL 白名单、下载大小限制和超时策略，避免 SSRF 或超大文件下载。
- 当前处理器要求请求具有非空 `Content-Length`，使用 chunked transfer 的请求可能不会被处理。
- 推理异常主要写入日志并结束结果流，客户端可能收到空结果而不是明确的 4xx/5xx 错误。
- Swagger UI 当前在所有环境启用，生产部署时建议按环境限制访问。
