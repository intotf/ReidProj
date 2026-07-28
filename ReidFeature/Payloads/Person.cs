using System.Numerics.Tensors;
using System.Runtime.InteropServices;

namespace ReidFeature.Payloads
{
    /// <summary>
    /// 人物信息
    /// </summary>
    public sealed class Person
    {
        /// <summary>
        /// 人物 ID
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// 所在分组 ID
        /// </summary>
        public string GroupId { get; set; }
        /// <summary>
        /// 人物名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 人脸特征向量
        /// </summary>
        public byte[]? FaceFeatures { get; set; } = [];

        /// <summary>
        /// 人物特征向量
        /// </summary>
        public byte[] ReidFeatures { get; set; } = [];

        /// <summary>
        /// 人物信息
        /// </summary>
        /// <param name="id">人物 ID</param>
        /// <param name="groupId">所在分组 ID</param>
        /// <param name="name">人物名称</param>
        /// <param name="faceFeatures">人脸特征向量</param>
        /// <param name="reidFeatures">人物特征向量</param>
        public Person(string id, string groupId, string name, byte[]? faceFeatures, byte[] reidFeatures)
        {
            Id = id;
            GroupId = groupId;
            Name = name;
            FaceFeatures = faceFeatures;
            ReidFeatures = reidFeatures;
        }

        /// <summary>
        /// 计算给定特征向量与人物人脸特征向量的余弦相似度
        /// </summary>
        /// <param name="features">要比较的特征向量</param>
        /// <returns>余弦相似度</returns>
        public float FaceSimilarity(ReadOnlySpan<byte> features)
        {
            return this.FaceFeatures == null || this.FaceFeatures.Length == 0 || features.IsEmpty
                ? 0f
                : CosineSimilarity(features, this.FaceFeatures);
        }

        /// <summary>
        /// 计算给定特征向量与人物所有特征向量的最大余弦相似度
        /// </summary>
        /// <param name="features">要比较的特征向量</param>
        /// <returns>最大余弦相似度</returns>
        public float ReidSimilarity(ReadOnlySpan<byte> features)
        {
            return CosineSimilarity(features, this.ReidFeatures);
        }


        private static float CosineSimilarity(ReadOnlySpan<byte> featuresA, ReadOnlySpan<byte> featuresB)
        {
            var vectorA = MemoryMarshal.Cast<byte, float>(featuresA);
            var vectorB = MemoryMarshal.Cast<byte, float>(featuresB);
            return CosineSimilarity(vectorA, vectorB);
        }

        private static float CosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
        {
            if (vectorA.Length != vectorB.Length)
            {
                throw new ArgumentException("特征向量维度不匹配");
            }

            var dot = TensorPrimitives.Dot(vectorA, vectorB);
            var normA = TensorPrimitives.Norm(vectorA);
            var normB = TensorPrimitives.Norm(vectorB);

            return normA == 0 || normB == 0 ? 0 : dot / (normA * normB);
        }
    }
}
