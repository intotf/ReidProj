using System.Numerics.Tensors;

namespace FaceFeature.Payloads
{
    /// <summary>
    /// 人脸分组中的人物信息 — 存储 ArcFace 512维人脸特征向量，用于人脸比对识别
    /// </summary>
    public sealed class FacePerson
    {
        /// <summary>人物 ID</summary>
        public string Id { get; set; }
        /// <summary>所在分组 ID</summary>
        public string GroupId { get; set; }
        /// <summary>人物名称</summary>
        public string Name { get; set; }
        /// <summary>人脸特征向量（ArcFace 512-dim float[]，内部处理使用）</summary>
        public float[] FaceFeatures { get; set; } = [];

        /// <summary>
        /// 人脸分组中的人物信息
        /// </summary>
        /// <param name="id"></param>
        /// <param name="groupId"></param>
        /// <param name="name"></param>
        /// <param name="faceFeatures"></param>
        public FacePerson(string id, string groupId, string name, float[] faceFeatures)
        {
            Id = id;
            GroupId = groupId;
            Name = name;
            FaceFeatures = faceFeatures;
        }

        /// <summary>
        /// 计算给定特征向量与人物人脸特征向量的余弦相似度
        /// </summary>
        /// <param name="features">要比较的特征向量</param>
        /// <returns>余弦相似度</returns>
        public float Similarity(ReadOnlySpan<float> features)
        {
            return FaceFeatures == null || FaceFeatures.Length == 0 || features.IsEmpty
                ? 0f
                : CosineSimilarity(features, FaceFeatures);
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
