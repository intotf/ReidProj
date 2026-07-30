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
        /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测), 2=StopOnFirstFrameHit(首帧命中即停), 4=UseGrayscaleReId(灰度ReID降低衣服颜色敏感度)</param>
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

            await foreach (var recognition in RecognizeAsync(persons, detections, similarityThreshold, cancellationToken))
            {
                yield return recognition;
            }
        }

        /// <summary>
        /// 处理图片 URL 识别请求：通过图片 URL 下载后检测，提取特征后与指定分组内的人物逐一比对，流式返回所有余弦相似度超过阈值的匹配人物
        /// </summary>
        /// <param name="personGroupProvider">人物分组提供者</param>
        /// <param name="urlRequest">URL 检测请求，包含 ImageUrl 属性</param>
        /// <param name="detectService">检测编排服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="httpClient">用于下载图片的 HTTP 客户端</param>
        /// <param name="groupId">分组 ID</param>
        /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测), 2=StopOnFirstFrameHit(首帧命中即停), 4=UseGrayscaleReId(灰度ReID降低衣服颜色敏感度)</param>
        /// <param name="similarityThreshold">相似度阈值</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>人物识别结果的异步流，每个匹配的人物作为一个元素逐个产出；若没有匹配人物则流为空</returns>
        public static async IAsyncEnumerable<PersonRecognition> HandleImageUrlAsync(
            IPersonGroupProvider personGroupProvider,
            UrlDetectRequest urlRequest,
            DetectService detectService,
            ILogger<Program> logger,
            HttpClient httpClient,
            string groupId,
            DetectionFlags flags = DetectionFlags.All,
            float similarityThreshold = SimilarityThreshold,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var persons = await personGroupProvider.GetPersonsAsync(groupId, cancellationToken);
            var detections = DetectHandler.HandleImageUrlAsync(urlRequest, detectService, logger, httpClient, flags, cancellationToken);

            await foreach (var recognition in RecognizeAsync(persons, detections, similarityThreshold, cancellationToken))
            {
                yield return recognition;
            }
        }

        /// <summary>
        /// 处理 H264 视频流识别请求：上传 H264 裸流帧，边解码边检测，提取特征后与指定分组内的人物逐一比对，流式返回所有余弦相似度超过阈值的匹配人物
        /// </summary>
        /// <param name="personGroupProvider">人物分组提供者</param>
        /// <param name="context">HTTP 上下文</param>
        /// <param name="detectService">检测编排服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="groupId">分组 ID</param>
        /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）；≤0 时解码输入流的所有帧</param>
        /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测), 2=StopOnFirstFrameHit(首帧命中即停), 4=UseGrayscaleReId(灰度ReID降低衣服颜色敏感度)</param>
        /// <param name="similarityThreshold">相似度阈值</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>人物识别结果的异步流，每个匹配的人物作为一个元素逐个产出；若没有匹配人物则流为空</returns>
        public static async IAsyncEnumerable<PersonRecognition> HandleH264StreamAsync(
            IPersonGroupProvider personGroupProvider,
            HttpContext context,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            int frameIntervalSeconds = 5,
            DetectionFlags flags = DetectionFlags.All,
            float similarityThreshold = SimilarityThreshold,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var persons = await personGroupProvider.GetPersonsAsync(groupId, cancellationToken);
            var detections = DetectHandler.HandleH264StreamAsync(context, detectService, logger, frameIntervalSeconds, flags, cancellationToken);

            await foreach (var recognition in RecognizeAsync(persons, detections, similarityThreshold, cancellationToken))
            {
                yield return recognition;
            }
        }

        /// <summary>
        /// 处理 H265 视频流识别请求：上传 H265 裸流帧，边解码边检测，提取特征后与指定分组内的人物逐一比对，流式返回所有余弦相似度超过阈值的匹配人物
        /// </summary>
        /// <param name="personGroupProvider">人物分组提供者</param>
        /// <param name="context">HTTP 上下文</param>
        /// <param name="detectService">检测编排服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="groupId">分组 ID</param>
        /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧）；≤0 时解码输入流的所有帧</param>
        /// <param name="flags">检测功能标志位。可组合值: 0=All(全部开启), 1=SkipFaceDetection(跳过人脸检测), 2=StopOnFirstFrameHit(首帧命中即停), 4=UseGrayscaleReId(灰度ReID降低衣服颜色敏感度)</param>
        /// <param name="similarityThreshold">相似度阈值</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>人物识别结果的异步流，每个匹配的人物作为一个元素逐个产出；若没有匹配人物则流为空</returns>
        public static async IAsyncEnumerable<PersonRecognition> HandleH265StreamAsync(
            IPersonGroupProvider personGroupProvider,
            HttpContext context,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            int frameIntervalSeconds = 5,
            DetectionFlags flags = DetectionFlags.All,
            float similarityThreshold = SimilarityThreshold,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var persons = await personGroupProvider.GetPersonsAsync(groupId, cancellationToken);
            var detections = DetectHandler.HandleH265StreamAsync(context, detectService, logger, frameIntervalSeconds, flags, cancellationToken);

            await foreach (var recognition in RecognizeAsync(persons, detections, similarityThreshold, cancellationToken))
            {
                yield return recognition;
            }
        }

        private static async IAsyncEnumerable<PersonRecognition> RecognizeAsync(
            Person[] persons,
            IAsyncEnumerable<PersonDetection> detections,
            float similarityThreshold,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var detection in detections.WithCancellation(cancellationToken))
            {
                var bestMatch = persons
                    .Select(p => new
                    {
                        Person = p,
                        FaceSimilarity = p.FaceSimilarity(detection.Face?.Features),
                        ReidSimilarity = p.ReidSimilarity(detection.Features)
                    })
                    .Where(i => i.FaceSimilarity > similarityThreshold || i.ReidSimilarity > similarityThreshold)
                    .OrderByDescending(i => i.FaceSimilarity)
                    .ThenByDescending(i => i.ReidSimilarity)
                    .FirstOrDefault();

                if (bestMatch != null)
                {
                    yield return new PersonRecognition(
                        bestMatch.Person.Id,
                        bestMatch.Person.GroupId,
                        bestMatch.Person.Name,
                        bestMatch.FaceSimilarity,
                        bestMatch.ReidSimilarity);
                }
            }
        }
    }
}
