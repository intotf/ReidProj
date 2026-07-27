using ReidFeature.Payloads;
using ReidFeature.Services;
using System.Runtime.CompilerServices;

namespace ReidFeature.Handlers
{
    /// <summary>
    /// 人物识别处理器
    /// </summary>
    public static class RecognizeHandler
    {
        private const float SimilarityThreshold = 0.9f;

        /// <summary>
        /// 处理图片识别请求：上传原始图片二进制数据，提取特征后与指定分组内的人物逐一比对，流式返回所有余弦相似度超过阈值的匹配人物
        /// </summary>
        /// <param name="personGroupProvider">人物分组提供者</param>
        /// <param name="request">HTTP 请求，其 Body 为原始图片二进制数据</param>
        /// <param name="detectService">检测编排服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="groupId">分组 ID</param>
        /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测), 2=StopOnFirstFrameHit(首帧命中即停)</param>
        /// <param name="similarityThreshold">相似度阈值</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>人物识别结果的异步流，每个匹配的人物作为一个元素逐个产出；若没有匹配人物则流为空</returns>
        public static async IAsyncEnumerable<PersonRecognition> HandleImageAsync(
            IPersonGroupProvider personGroupProvider,
            HttpRequest request,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            DetectionFlags flags = DetectionFlags.All,
            float similarityThreshold = SimilarityThreshold,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var persons = await personGroupProvider.GetPersonsAsync(groupId, cancellationToken);
            var detections = DetectHandler.HandleImageAsync(request, detectService, logger, flags, cancellationToken);

            await foreach (var detection in detections.WithCancellation(cancellationToken))
            {
                var bestMatch = persons
                    .Select(p => new { Person = p, Similarity = p.ReidSimilarity(detection.Features) })
                    .MaxBy(p => p.Similarity);

                if (bestMatch != null && bestMatch.Similarity >= similarityThreshold)
                {
                    yield return new PersonRecognition(
                        bestMatch.Person.Id,
                        bestMatch.Person.GroupId,
                        bestMatch.Person.Name,
                        bestMatch.Similarity);
                }
            }
        }
    }
}
