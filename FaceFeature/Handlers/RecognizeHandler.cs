using FaceFeature.Payloads;
using FaceFeature.Services;
using System.Runtime.CompilerServices;

namespace FaceFeature.Handlers
{
    /// <summary>
    /// 人脸识别处理器 — 检测到的人脸与分组中的人脸逐一比对，流式返回匹配结果
    /// </summary>
    public static class RecognizeHandler
    {
        private const float SimilarityThreshold = 0.9f;

        /// <summary>
        /// 处理图片识别请求：上传原始图片二进制数据，检测最佳人脸并提取特征后与指定分组内的人物比对
        /// </summary>
        /// <param name="faceGroupProvider">人脸分组提供者</param>
        /// <param name="request">HTTP 请求，其 Body 为原始图片二进制数据</param>
        /// <param name="detectService">检测编排服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="groupId">分组 ID</param>
        /// <param name="similarityThreshold">相似度阈值</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task<FaceRecognition?> HandleImageAsync(
            IFaceGroupProvider faceGroupProvider,
            HttpRequest request,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            float similarityThreshold = SimilarityThreshold,
            CancellationToken cancellationToken = default)
        {
            var persons = await faceGroupProvider.GetPersonsAsync(groupId, cancellationToken);
            var detection = await DetectHandler.HandleImageAsync(request, detectService, logger, cancellationToken);

            return MatchSingle(persons, detection, similarityThreshold);
        }

        /// <summary>
        /// 处理图片 URL 识别请求：通过图片 URL 下载后检测最佳人脸并与分组人物比对
        /// </summary>
        public static async Task<FaceRecognition?> HandleImageUrlAsync(
            IFaceGroupProvider faceGroupProvider,
            UrlDetectRequest urlRequest,
            DetectService detectService,
            ILogger<Program> logger,
            HttpClient httpClient,
            string groupId,
            float similarityThreshold = SimilarityThreshold,
            CancellationToken cancellationToken = default)
        {
            var persons = await faceGroupProvider.GetPersonsAsync(groupId, cancellationToken);
            var detection = await DetectHandler.HandleImageUrlAsync(urlRequest, detectService, logger, httpClient, cancellationToken);

            return MatchSingle(persons, detection, similarityThreshold);
        }

        /// <summary>
        /// 处理 H264 视频流识别请求
        /// </summary>
        public static async IAsyncEnumerable<FaceRecognition> HandleH264StreamAsync(
            IFaceGroupProvider faceGroupProvider,
            HttpContext context,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            double frameIntervalSeconds = 5,
            float similarityThreshold = SimilarityThreshold,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var persons = await faceGroupProvider.GetPersonsAsync(groupId, cancellationToken);
            var detections = DetectHandler.HandleH264StreamAsync(context, detectService, logger, frameIntervalSeconds, cancellationToken);

            await foreach (var recognition in RecognizeAsync(persons, detections, similarityThreshold, cancellationToken))
            {
                yield return recognition;
            }
        }

        /// <summary>
        /// 处理 H265 视频流识别请求
        /// </summary>
        public static async IAsyncEnumerable<FaceRecognition> HandleH265StreamAsync(
            IFaceGroupProvider faceGroupProvider,
            HttpContext context,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            double frameIntervalSeconds = 5,
            float similarityThreshold = SimilarityThreshold,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var persons = await faceGroupProvider.GetPersonsAsync(groupId, cancellationToken);
            var detections = DetectHandler.HandleH265StreamAsync(context, detectService, logger, frameIntervalSeconds, cancellationToken);

            await foreach (var recognition in RecognizeAsync(persons, detections, similarityThreshold, cancellationToken))
            {
                yield return recognition;
            }
        }

        private static async IAsyncEnumerable<FaceRecognition> RecognizeAsync(
            FacePerson[] persons,
            IAsyncEnumerable<FaceDetection> detections,
            float similarityThreshold,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var detection in detections.WithCancellation(cancellationToken))
            {
                var recognition = MatchPerson(persons, detection, similarityThreshold);
                if (recognition != null)
                    yield return recognition;
            }
        }

        private static FaceRecognition? MatchSingle(
            FacePerson[] persons,
            FaceDetection? detection,
            float similarityThreshold)
        {
            if (detection == null) return null;
            return MatchPerson(persons, detection, similarityThreshold);
        }

        private static FaceRecognition? MatchPerson(
            FacePerson[] persons,
            FaceDetection detection,
            float similarityThreshold)
        {
            var bestMatch = persons
                .Select(p => new
                {
                    Person = p,
                    Similarity = p.Similarity(detection.Features)
                })
                .Where(i => i.Similarity > similarityThreshold)
                .OrderByDescending(i => i.Similarity)
                .FirstOrDefault();

            if (bestMatch != null)
            {
                return new FaceRecognition(
                    bestMatch.Person.Id,
                    bestMatch.Person.GroupId,
                    bestMatch.Person.Name,
                    bestMatch.Similarity);
            }

            return null;
        }
    }
}
