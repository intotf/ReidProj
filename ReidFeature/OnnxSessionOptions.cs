namespace ReidFeature;

/// <summary>
/// ONNX Runtime SessionOptions 配置模型，支持模型独立配置，可从 appsettings.json 绑定
/// </summary>
public sealed class OnnxSessionOptions
{
    /// <summary>YOLO 人物检测配置</summary>
    public Microsoft.ML.OnnxRuntime.SessionOptions Yolo { get; set; } = Default();

    /// <summary>ReID 特征提取配置</summary>
    public Microsoft.ML.OnnxRuntime.SessionOptions ReId { get; set; } = Default();

    /// <summary>MoveNet 姿态估计配置</summary>
    public Microsoft.ML.OnnxRuntime.SessionOptions Pose { get; set; } = Default();

    private static Microsoft.ML.OnnxRuntime.SessionOptions Default()
    {
        return new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = Microsoft.ML.OnnxRuntime.ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
    }
}
