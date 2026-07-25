namespace ReidFeature.Payloads;

/// <summary>
/// 检测响应的包装
/// </summary>
/// <param name="Persons">检测到的人物列表</param>
public sealed record DetectResponse(
    PersonDetection[] Persons
);
