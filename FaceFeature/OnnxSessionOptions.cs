namespace FaceFeature;

/// <summary>
/// ONNX Runtime SessionOptions 配置模型，支持两个模型各自的独立配置，可从 appsettings.json 绑定
/// </summary>
public sealed class OnnxSessionOptions
{
    /// <summary>人脸检测配置（SCRFD-10g）</summary>
    public Microsoft.ML.OnnxRuntime.SessionOptions Face { get; set; } = Default();

    /// <summary>人脸特征提取会话配置（ArcFace glintr100 / w600k_r50）</summary>
    public Microsoft.ML.OnnxRuntime.SessionOptions FaceRec { get; set; } = Default();

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
