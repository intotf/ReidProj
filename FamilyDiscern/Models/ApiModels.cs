using System;
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
/// 单个视频段的注册信息（批量注册结果中的明细项）
/// </summary>
public class EnrollSegmentInfo
{
    public string FileName { get; set; } = "";
    public int TrackId { get; set; }
}

/// <summary>
/// 批量注册（同一人多段注册）结果
/// </summary>
public class EnrollBatchResult
{
    public string MemberId { get; set; } = "";
    public string Name { get; set; } = "";
    public string GroupId { get; set; } = "";
    public int SegmentCount { get; set; }
    public List<EnrollSegmentInfo> Segments { get; set; } = [];
}

/// <summary>
/// 成员合并（去重）请求
/// </summary>
public class MergeMembersRequest
{
    public string TargetMemberId { get; set; } = "";
    public List<string> MergeMemberIds { get; set; } = [];
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
    public DateTime EnrolledAt { get; set; }
    public string Mp4Path { get; set; } = "";
    public double FrameIntervalSeconds { get; set; }
    public string RegisterTime { get; set; } = "";
}

[JsonSerializable(typeof(EnrollResult))]
[JsonSerializable(typeof(PersonRecognition))]
[JsonSerializable(typeof(List<FamilyMember>))]
[JsonSerializable(typeof(EnrollBatchResult))]
[JsonSerializable(typeof(EnrollSegmentInfo))]
[JsonSerializable(typeof(MergeMembersRequest))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ApiJsonContext : JsonSerializerContext;
