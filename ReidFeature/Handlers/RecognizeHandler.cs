using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ReidFeature.Handlers
{
    /// <summary>
    /// 家庭成员识别处理器 — 视频流 → 四维融合 → 一个最佳匹配
    /// </summary>
    public static class RecognizeHandler
    {
        /// <summary>命中阈值：四维融合分数超过此值才考虑命中</summary>
        private const float HitThreshold = 0.62f;

        /// <summary>命中与次高分的最小差距</summary>
        private const float MarginThreshold = 0.08f;

        /// <summary>四维匹配权重</summary>
        private const float WCloth = 0.20f;
        private const float WHead = 0.30f;
        private const float WBody = 0.30f;
        private const float WGait = 0.20f;

        /// <summary>
        /// 处理 H264 视频流识别 — 收集所有帧后四维融合匹配，只返回最佳结果
        /// </summary>
        public static async Task<PersonRecognition> HandleH264StreamAsync(
            IFamilyMemberProvider familyProvider,
            HttpContext context,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            int frameIntervalSeconds = 5,
            DetectionFlags flags = DetectionFlags.All,
            CancellationToken cancellationToken = default)
        {
            return await RecognizeVideoAsync(
                familyProvider, context.Request, detectService, logger,
                groupId, VideoCodec.H264, frameIntervalSeconds, flags, cancellationToken);
        }

        /// <summary>
        /// 处理 H265 视频流识别 — 收集所有帧后四维融合匹配，只返回最佳结果
        /// </summary>
        public static async Task<PersonRecognition> HandleH265StreamAsync(
            IFamilyMemberProvider familyProvider,
            HttpContext context,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            int frameIntervalSeconds = 5,
            DetectionFlags flags = DetectionFlags.All,
            CancellationToken cancellationToken = default)
        {
            return await RecognizeVideoAsync(
                familyProvider, context.Request, detectService, logger,
                groupId, VideoCodec.H265, frameIntervalSeconds, flags, cancellationToken);
        }

        private static async Task<PersonRecognition> RecognizeVideoAsync(
            IFamilyMemberProvider familyProvider,
            HttpRequest request,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            VideoCodec codec,
            int frameIntervalSeconds,
            DetectionFlags flags,
            CancellationToken cancellationToken)
        {
            if (request.ContentLength == null || request.ContentLength == 0)
            {
                Log.RequestBodyEmpty(logger);
                return new PersonRecognition("", groupId, "stranger", 0f, 0f);
            }

            // 1. 获取 Gallery 成员
            var members = await familyProvider.GetMembersAsync(groupId, cancellationToken);
            if (members.Length == 0)
            {
                return new PersonRecognition("", groupId, "stranger", 0f, 0f);
            }

            // 2. 解码视频流，逐帧处理
            detectService.Reset();
            int frameIdx = 0;

            var enumerable = VideoDecoder.DecodeFramesAsync(
                request.Body, codec, logger, frameIntervalSeconds, cancellationToken);
            await using var enumerator = enumerable.GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                Image<Rgb24> image;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        break;
                    image = enumerator.Current;
                }
                catch (Exception ex)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Log.VideoDecodeFailed(logger, ex);
                    return new PersonRecognition("", groupId, "stranger", 0f, 0f);
                }

                using (image)
                {
                    detectService.ProcessVideoFrame(image, frameIdx++);
                }
            }

            // 3. 获取已完成的 Track 融合结果
            var tracks = detectService.FlushCompletedTracks();

            // 4. 对每个 Track 的四维特征包与所有 Gallery 成员匹配
            var bestScore = 0f;
            var secondBestScore = 0f;
            Person? bestPerson = null;

            foreach (var track in tracks)
            {
                if (track.FeaturePack is null)
                    continue;

                foreach (var member in members)
                {
                    if (member.FeaturePack is null)
                        continue;

                    float score = TrackFeaturePack.WeightedCosineSimilarity(
                        track.FeaturePack, member.FeaturePack);

                    if (score > bestScore)
                    {
                        secondBestScore = bestScore;
                        bestScore = score;
                        bestPerson = member;
                    }
                    else if (score > secondBestScore)
                    {
                        secondBestScore = score;
                    }
                }
            }

            // 5. 判决：最高分 > 0.62 且与次高分差 > 0.08
            if (bestPerson != null && bestScore > HitThreshold &&
                (bestScore - secondBestScore) > MarginThreshold)
            {
                Log.RecognitionResult(logger, bestPerson.Name, bestScore, 0);
                return new PersonRecognition(
                    bestPerson.Id, groupId, bestPerson.Name, bestScore, 0f);
            }

            Log.RecognitionResult(logger, "stranger", bestScore, 0);
            return new PersonRecognition("", groupId, "stranger", bestScore, 0f);
        }
    }
}
