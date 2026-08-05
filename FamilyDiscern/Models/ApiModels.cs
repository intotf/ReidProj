using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyDiscern.Models;

/// <summary>
/// 注册结果
/// </summary>
public class EnrollResult
{
    public string MemberId { get; set; } = "";
    public string Name { get; set; } = "";
    public string GroupId { get; set; } = "";
}

/// <summary>
/// 识别结果
/// </summary>
public class PersonRecognition
{
    public string Id { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Name { get; set; } = "";
    public float Score { get; set; }
    public float ClothScore { get; set; }
    public float HeadScore { get; set; }
    public float BodyScore { get; set; }
    public float GaitScore { get; set; }
}

/// <summary>
/// 家庭成员信息（列表接口返回）
/// </summary>
public class FamilyMember
{
    public string Id { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Mp4Path { get; set; } = "";
    public double FrameIntervalSeconds { get; set; }
    public string RegisterTime { get; set; } = "";
}

[JsonSerializable(typeof(EnrollResult))]
[JsonSerializable(typeof(PersonRecognition))]
[JsonSerializable(typeof(List<FamilyMember>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class ApiJsonContext : JsonSerializerContext;
