namespace ReidFeature.Payloads;

/// <summary>
/// 裁剪类型
/// </summary>
public enum CropType
{
    /// <summary>全身裁剪（默认，bbox 全范围）</summary>
    FullBody,

    /// <summary>头肩区域裁剪（取 bbox 上半 38%，换衣鲁棒）</summary>
    HeadShoulder,
}
