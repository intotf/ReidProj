namespace FaceFeature.Payloads;

/// <summary>
/// 人脸注册结果
/// </summary>
/// <param name="Success">是否注册成功</param>
/// <param name="Face">注册成功时返回的人脸信息（含特征）</param>
/// <param name="Error">注册失败时的错误描述</param>
public sealed record FaceRegistrationResult(bool Success, FaceInfo? Face, string? Error)
{
    /// <summary>创建注册成功结果</summary>
    public static FaceRegistrationResult Ok(FaceInfo face)
    {
        return new FaceRegistrationResult(true, face, null);
    }

    /// <summary>创建注册失败结果</summary>
    public static FaceRegistrationResult Failed(string error)
    {
        return new FaceRegistrationResult(false, null, error);
    }
}
