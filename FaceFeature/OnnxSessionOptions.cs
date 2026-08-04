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

    /// <summary>人脸特征模型文件名（models 目录下），默认 glintr100.onnx（ArcFace R100），可切换 w600k_r50.onnx 等</summary>
    public string FaceRecognitionModelName { get; set; } = "glintr100.onnx";

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
