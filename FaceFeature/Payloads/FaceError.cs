namespace FaceFeature.Payloads;

/// <summary>人脸管理接口的错误响应</summary>
/// <param name="Error">错误描述</param>
public sealed record FaceError(string Error);
