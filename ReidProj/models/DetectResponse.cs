namespace ReidProj.Models;

/// <summary>
/// 检测响应的包装
/// </summary>
public sealed record DetectResponse(
    PersonDetection[] Persons
);
