using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceFeature.Services;

/// <summary>
/// 对齐后的 112×112 人脸与清晰度分数（Aligned 由调用方负责释放）
/// </summary>
/// <param name="Aligned">对齐后的人脸图（112×112）</param>
/// <param name="Sharpness">清晰度分数（Laplacian 方差，越大越清晰）</param>
public sealed record FaceExtraction(Image<Rgb24> Aligned, float Sharpness) : IDisposable
{
    /// <summary>释放对齐人脸图</summary>
    public void Dispose() => Aligned.Dispose();
}
