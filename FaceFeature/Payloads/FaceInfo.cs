using System.Text.Json.Serialization;

namespace FaceFeature.Payloads;

/// <summary>
/// 已注册人脸信息（注册 / 查询响应）
/// </summary>
/// <param name="Id">人脸 ID（注册时生成，全局唯一）</param>
/// <param name="GroupId">所在分组 ID</param>
/// <param name="Name">人物名称</param>
/// <param name="Confidence">人脸检测置信度</param>
/// <param name="Sharpness">清晰度分数（对齐后人脸 Laplacian 方差）</param>
/// <param name="Bbox">人脸边界框（原图坐标）</param>
/// <param name="RegisteredAt">注册时间</param>
/// <param name="Features">512 维特征向量（base64 编码）；列表接口不返回</param>
public sealed record FaceInfo(
    string Id,
    string GroupId,
    string Name,
    float Confidence,
    float Sharpness,
    BoundingBox Bbox,
    DateTime RegisteredAt,
    [property: JsonConverter(typeof(FloatArrayBase64Converter))] float[]? Features = null);
