using System.ComponentModel.DataAnnotations;

namespace ReidFeature.Payloads;

/// <summary>
/// 通过 URL 提交检测请求的请求体
/// </summary>
/// <param name="ImageUrl">图片的公开可访问 URL（如 S3 预签名 URL）</param>
public sealed record UrlDetectRequest([Url] string ImageUrl);