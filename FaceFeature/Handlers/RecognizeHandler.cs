using FaceFeature.Helpers;
using FaceFeature.Payloads;
using FaceFeature.Services;

namespace FaceFeature.Handlers
{
    /// <summary>
    /// 人脸识别处理器 — 视频多帧融合后与分组人物比对，一个视频输入只产出一个识别结果
    /// </summary>
    public static class RecognizeHandler
    {
        // 默认阈值按门禁摄像头实测标定：同人全量融合约 0.84，单帧最高约 0.88；
        // 正式上线前请用真实异人样本重新标定（异人正样本低于 0.5 时保持 0.6）
        private const float SimilarityThreshold = 0.6f;

        /// <summary>
        /// 处理 H264 视频流识别请求：融合整段流后返回单个识别结果
        /// </summary>
        /// <param name="faceGroupService">人脸分组管理服务</param>
        /// <param name="context">HTTP 上下文</param>
        /// <param name="detectService">检测编排服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="groupId">分组 ID</param>
        /// <param name="frameIntervalSeconds">帧间隔秒数</param>
        /// <param name="similarityThreshold">相似度阈值</param>
        /// <param name="fusionFrames">融合帧数上限（&gt;0）</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task<FaceRecognition?> HandleH264StreamAsync(
            FaceGroupService faceGroupService,
            HttpContext context,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            double frameIntervalSeconds = 0.5,
            float similarityThreshold = SimilarityThreshold,
            int fusionFrames = 30,
            CancellationToken cancellationToken = default)
        {
            return await RecognizeAsync(
                faceGroupService, context.Request, detectService, logger, groupId,
                VideoCodec.H264, frameIntervalSeconds, similarityThreshold, fusionFrames, cancellationToken);
        }

        /// <summary>
        /// 处理 H265 视频流识别请求：融合整段流后返回单个识别结果
        /// </summary>
        public static async Task<FaceRecognition?> HandleH265StreamAsync(
            FaceGroupService faceGroupService,
            HttpContext context,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            double frameIntervalSeconds = 0.5,
            float similarityThreshold = SimilarityThreshold,
            int fusionFrames = 30,
            CancellationToken cancellationToken = default)
        {
            return await RecognizeAsync(
                faceGroupService, context.Request, detectService, logger, groupId,
                VideoCodec.H265, frameIntervalSeconds, similarityThreshold, fusionFrames, cancellationToken);
        }

        private static async Task<FaceRecognition?> RecognizeAsync(
            FaceGroupService faceGroupService,
            HttpRequest request,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            VideoCodec codec,
            double frameIntervalSeconds,
            float similarityThreshold,
            int fusionFrames,
            CancellationToken cancellationToken)
        {
            var persons = await faceGroupService.GetPersonsAsync(groupId, cancellationToken);
            var frames = detectService.DetectFramesAsync(request.Body, codec, frameIntervalSeconds, cancellationToken);

            var fused = await FaceVideoFusion.FuseAsync(
                frames, fusionFrames > 0 ? fusionFrames : int.MaxValue, cancellationToken);
            if (fused is null)
            {
                return null;
            }

            var match = MatchPerson(persons, fused.Features, similarityThreshold);
            Log.FaceFusionCompleted(logger, fused.FrameCount, fused.Early, match?.FaceSimilarity ?? 0f);
            return match;
        }

        private static FaceRecognition? MatchPerson(
            FacePerson[] persons,
            ReadOnlySpan<float> features,
            float similarityThreshold)
        {
            FacePerson? bestPerson = null;
            float bestSimilarity = 0f;
            foreach (var person in persons)
            {
                float similarity = person.Similarity(features);
                if (similarity > similarityThreshold && similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestPerson = person;
                }
            }

            return bestPerson is null
                ? null
                : new FaceRecognition(bestPerson.Id, bestPerson.GroupId, bestPerson.Name, bestSimilarity);
        }
    }
}
