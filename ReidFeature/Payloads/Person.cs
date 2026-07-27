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
        /// 人物特征向量
        /// </summary>
        public List<byte[]> ReidFeatures { get; set; } = [];

        /// <summary>
        /// 人物信息
        /// </summary>
        /// <param name="id">人物 ID</param>
        /// <param name="groupId">所在分组 ID</param>
        /// <param name="name">人物名称</param>
        /// <param name="reidFeatures">人物特征向量</param>
        public Person(string id, string groupId, string name, List<byte[]> reidFeatures)
        {
            Id = id;
            GroupId = groupId;
            Name = name;
            ReidFeatures = reidFeatures;
        }

        /// <summary>
        /// 计算给定特征向量与人物所有特征向量的最大余弦相似度
        /// </summary>
        /// <param name="features">要比较的特征向量</param>
        /// <returns>最大余弦相似度</returns>
        public float ReidSimilarity(ReadOnlySpan<byte> features)
        {
            float maxSimilarity = 0f;
            foreach (var reidFeature in this.ReidFeatures)
            {
                var similarity = CosineSimilarity(features, reidFeature);
                if (similarity > maxSimilarity)
                {
                    maxSimilarity = similarity;
                }
            }
            return maxSimilarity;
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
