namespace FaceFeature.Payloads;

/// <summary>人脸删除接口的响应</summary>
/// <param name="Deleted">是否删除成功</param>
public sealed record FaceDeleteResponse(bool Deleted);
