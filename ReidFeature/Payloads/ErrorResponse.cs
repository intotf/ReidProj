namespace ReidFeature.Payloads;

/// <summary>
/// 错误响应
/// </summary>
/// <param name="Error">错误描述</param>
public sealed record ErrorResponse(string Error);
