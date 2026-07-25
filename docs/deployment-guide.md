# ReidProj 部署指南

## 推荐部署 — 阿里云 FC (Custom Container, 2C 2G)

### 规格说明

| 规格 | 值 |
|---|---|
| 运行环境 | Custom Container |
| CPU | 2 vCPU |
| 内存 | 2048 MB（2G） |
| 监听端口 | 9000 |
| 超时时间 | 10s |
| 单实例并发度 | 2（推理 CPU 密集，多路复用互相拖慢） |
 

### 测试

```bash
curl -X POST \
    -H "Content-Type: application/octet-stream" \
    --data-binary @test.jpg \
    https://你的FC域名/detect

# 带标志跳过人脸检测
curl -X POST \
    -H "Content-Type: application/octet-stream" \
    --data-binary @test.jpg \
    'https://你的FC域名/detect?flags=SkipFaceDetection'
```

---

## 备选方案 — 腾讯云 SCF / AWS Lambda

### 推荐规格

| 规格 | 值 | 说明 |
|---|---|---|
| 内存 | 2GB | AOT 运行时约 800MB-1.2GB，2G 充裕 |
| vCPU | 2 核 | IntraOpNumThreads=0 自动适配 |
| 实例并发度 | 2 | CPU 密集不适合复用 |
| 超时时间 | 10s | |

### 注意事项

- 强烈建议开启 PublishAot，非 AOT 冷启动更慢
- 模型文件约 150MB，非 Custom Container 方案需确认代码包大小限制

---

## 配置说明

### OnnxSessionOptions

默认 IntraOpNumThreads=0（自动使用全部 CPU），2C 场景下自动利用 2 核。

可通过环境变量覆盖（FC 控制台配置）：

| 环境变量 | 作用 |
|---|---|
| Onnx__Yolo__IntraOpNumThreads | YOLO 人物检测 |
| Onnx__ReId__IntraOpNumThreads | ReID 特征提取 |
| Onnx__Face__IntraOpNumThreads | 人脸检测 |

FC 采用双下划线 `__` 分隔符映射 appsettings.json 层级。
