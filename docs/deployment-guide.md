# ReidProj 部署指南

## 云函数部署（腾讯云 SCF）

### 推荐规格

| 规格 | 值 | 说明 |
|---|---|---|
| 内存 | **4GB** | 模型 ~180MB + CLR + 运行时，2GB 也能跑但延迟高 |
| vCPU | **2 核** | 与 `IntraOpNumThreads=2` 完全匹配，CPU 利用率最充分 |
| 实例并发度 | **1** | 推理全程 CPU 密集，多路复用会互相拖慢 |
| 预置并发 | **2-3** | 避免冷启动（模型加载 1-2s） |
| 最大并发度 | **10-20** | 稳定区间，单请求 ~350ms，总吞吐约 30-60 QPS |
| 超时时间 | **60s** | 防止复杂图片卡断 |
| 代码包 | 建议将模型放在 `models/` 目录并打包入 zip |

### 性能参考

| 场景 | 延迟 | 说明 |
|---|---|---|
| 单请求（3 人） | ~350ms | 4GB/2vCPU 规格 |
| 1 并发 | ~350ms | 稳定 |
| 5 并发 | ~700-900ms | 轻微退化 |
| 10 并发 | ~1.5-2s | 可接受，延迟显著增加 |
| 冷启动 | ~2-3s | 加载两个 ONNX 模型 |
| 预置实例 | ~350ms | 预置后无冷启动 |

### 预算估算

```
单请求成本 ≈ 4GB × 0.35s × ¥0.000111/GB-s ≈ ¥0.000155

日常低负载（1000 请求/天）：  ~¥4.6/月
中等负载（10万请求/天）：      ~¥465/月
持续满载（30 QPS 全天）：      ~¥4000/月
```

### SCF 配置注意事项

1. **模型路径**：代码已有 `AppContext.BaseDirectory/models/` 和 `./models/` 双重 fallback，SCF 下自动适配
2. **冷启动优化**：务必配置预置并发，否则首次请求需等待 2-3s 模型加载
3. **HTTP 触发器**：API 网关 → SCF，`POST /detect` 接口不变
4. **日志**：`ILogger` 输出自动被 SCF 捕获到日志平台

### 部署步骤

```bash
# 1. 发布
dotnet publish -c Release -o publish

# 2. 确认模型文件已复制到 publish/models/
ls publish/models/

# 3. 压缩（含模型）
cd publish
zip -r ../deploy.zip .

# 4. 上传到 SCF，配置：
#    运行环境: .NET 8 (SCF 支持)
#    内存: 4096MB
#    超时: 60s
#    启动命令: dotnet ReidProj.dll
#    预置并发: 3
```

## 本地部署

### 最小配置

| 规格 | 预计延迟 |
|---|---|
| 4 核 CPU, 4GB 内存 | ~350ms/请求 |
| 2 核 CPU, 2GB 内存 | ~800ms/请求 |

### Kestrel 配置

当前已配置（`Program.cs`）：

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:9000"
      }
    },
    "Limits": {
      "MaxRequestBodySize": 20971520  // 20MB
    }
  }
}
```

## SessionOptions 说明

当前两模型统一使用：

```csharp
opts.IntraOpNumThreads = 1;       // 单线程推理，避免线程迁移缓存冷失效
opts.InterOpNumThreads = 1;       // 子图间串行，避免线程膨胀
opts.ExecutionMode = ORT_SEQUENTIAL; // 按图序逐子图执行
opts.GraphOptimizationLevel = ORT_ENABLE_ALL; // 全图优化
```

部署到不同规格的云函数时，建议调整 `IntraOpNumThreads`：

| vCPU | IntraOpNumThreads | 说明 |
|---|---|---|
| 1 | 1 | 单核，无并行 |
| 2 | 1 | 推荐！CPU Conv 算子已有 BLAS 内部并行，ORT 开多线程反而导致线程颠簸 |
| 4 | 2 | 4 核可尝试 2，需实测稳定性 |
| >4 | 2 | 超过 4 核时 2 线程可能收益，需压力测试验证 |
