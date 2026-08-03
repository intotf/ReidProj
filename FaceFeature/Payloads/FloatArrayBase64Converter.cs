using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaceFeature.Payloads;

/// <summary>
/// float[] 特征向量的 JSON 转换：序列化为 base64（原始字节），反序列化还原为 float[]。
/// 使内部处理链路统一使用 float[]，byte[] 仅存在于 JSON 序列化层。
/// </summary>
internal sealed class FloatArrayBase64Converter : JsonConverter<float[]>
{
    /// <summary>把 base64 字符串反序列化为 float[] 特征向量</summary>
    public override float[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return ToFloats(reader.GetBytesFromBase64());
    }

    /// <summary>把 float[] 特征向量序列化为 base64 字符串（原始字节）</summary>
    public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
    {
        writer.WriteBase64StringValue(ToBytes(value));
    }

    /// <summary>float[] → byte[]（原始小端字节，供 JSON base64 序列化）</summary>
    private static byte[] ToBytes(ReadOnlySpan<float> features)
    {
        var bytes = new byte[features.Length * sizeof(float)];
        MemoryMarshal.Cast<float, byte>(features).CopyTo(bytes);
        return bytes;
    }

    /// <summary>byte[] → float[]（从 JSON base64 解码出的原始字节还原）</summary>
    private static float[] ToFloats(ReadOnlySpan<byte> bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        MemoryMarshal.Cast<byte, float>(bytes).CopyTo(floats);
        return floats;
    }
}
