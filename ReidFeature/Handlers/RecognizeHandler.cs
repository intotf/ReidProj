using ReidFeature.Helpers;
using ReidFeature.Payloads;
using ReidFeature.Services;

namespace ReidFeature.Handlers
{
    /// <summary>
    /// 家庭成员识别处理器 —— 视频流（编码自动识别）→ 四维融合 → 一个最佳匹配
    /// </summary>
    public static class RecognizeHandler
    {
        /// <summary>基础命中阈值：无歧义（margin 满足）时总分须超过此值才命中</summary>
        private const float HitThreshold = 0.88f;

        /// <summary>命中与次高分的最小差距（歧义判定）</summary>
        private const float MarginThreshold = 0.08f;

        /// <summary>高分兜底阈值：出现歧义（margin 不满足）时，总分仍达到此值则直接命中（同一人多条目场景）</summary>
        private const float HighConfidenceThreshold = 0.965f;

        /// <summary>
        /// 处理视频流识别（H264/H265 裸流均可，编码自动识别）—— 收集所有帧后四维融合匹配，只返回最佳结果
        /// </summary>
        /// <param name="familyProvider">家庭成员提供者（Gallery 数据源）</param>
        /// <param name="context">HTTP 上下文</param>
        /// <param name="detectService">检测编排服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="groupId">分组 ID</param>
        /// <param name="frameIntervalSeconds">帧间隔秒数（每隔 N 秒解码一帧），如 0.5 表示每 0.5 秒一帧；≤0 时解码全部帧</param>
        /// <param name="wCloth">全身 ReID 权重（默认 0.30）</param>
        /// <param name="wHead">头肩 ReID 权重（默认 0.40）</param>
        /// <param name="wBody">体型标量权重（默认 0.20）</param>
        /// <param name="wGait">步态标量权重（默认 0.10）</param>
        /// <param name="highConfidenceThreshold">高分兜底阈值（默认 0.965，可通过查询参数动态调整，取值 [0,1]）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>最佳匹配的人物识别结果；请求体为空、gallery 无成员或视频解码失败时返回 null</returns>
        public static async Task<PersonRecognition?> HandleStreamAsync(
            IFamilyMemberProvider familyProvider,
            HttpContext context,
            DetectService detectService,
            ILogger<Program> logger,
            string groupId,
            double frameIntervalSeconds = 0.5,
            float wCloth = TrackFeaturePack.WCloth,
            float wHead = TrackFeaturePack.WHead,
            float wBody = TrackFeaturePack.WBody,
            float wGait = TrackFeaturePack.WGait,
            float highConfidenceThreshold = HighConfidenceThreshold,
            CancellationToken cancellationToken = default)
        {
            // 防御无效参数：NaN/±Infinity 统一按 0（解码全部帧）处理，并 clamp 到合理上限
            if (double.IsNaN(frameIntervalSeconds) || double.IsInfinity(frameIntervalSeconds))
            {
                frameIntervalSeconds = 0;
            }
            frameIntervalSeconds = Math.Clamp(frameIntervalSeconds, 0, 3600);

            // 兜底阈值：NaN/±Infinity 用默认值，并 clamp 到 [0,1]
            if (float.IsNaN(highConfidenceThreshold) || float.IsInfinity(highConfidenceThreshold))
            {
                highConfidenceThreshold = HighConfidenceThreshold;
            }
            highConfidenceThreshold = Math.Clamp(highConfidenceThreshold, 0f, 1f);

            // 1. 获取 Gallery 成员
            var members = await familyProvider.GetMembersAsync(groupId, cancellationToken);
            if (members.Length == 0)
            {
                return null;
            }

            // 2. 解码视频流并逐帧检测/跟踪（统一由 DetectService 处理）
            if (!await detectService.ProcessVideoStreamAsync(
                context.Request, logger, frameIntervalSeconds, cancellationToken))
            {
                return null;
            }

            // 3. 获取已完成的 Track 融合结果
            var tracks = detectService.FlushCompletedTracks();

            // 4. 对每个 Track 的四维特征包与所有 Gallery 成员匹配
            var bestScore = 0f;
            var secondBestScore = 0f;   // 最佳 Track 内的次佳成员得分（仅用于诊断日志）
            Person? bestPerson = null;
            int bestTrackId = 0;
            var bestScores = new TrackSimilarityScores(0f, 0f, 0f, 0f);

            foreach (var track in tracks)
            {
                if (track.FeaturePack is null)
                {
                    continue;
                }

                // 每个 Track 独立计算最佳/次佳成员，避免跨 Track（不同人）分数互相污染
                float trackBest = 0f;
                float trackSecond = 0f;
                Person? trackBestPerson = null;
                var trackBestScores = new TrackSimilarityScores(0f, 0f, 0f, 0f);

                foreach (var member in members)
                {
                    if (member.FeaturePack is null)
                    {
                        continue;
                    }

                    var scores = TrackFeaturePack.ComputeScores(
                        track.FeaturePack, member.FeaturePack);
                    float score = scores.ComputeTotal(wCloth, wHead, wBody, wGait);

                    if (score > trackBest)
                    {
                        trackSecond = trackBest;
                        trackBest = score;
                        trackBestPerson = member;
                        trackBestScores = scores;
                    }
                    else if (score > trackSecond)
                    {
                        trackSecond = score;
                    }
                }

                // 取所有 Track 中最优匹配对（保留次佳分用于诊断日志）
                if (trackBest > bestScore)
                {
                    bestScore = trackBest;
                    secondBestScore = trackSecond;
                    bestPerson = trackBestPerson;
                    bestTrackId = track.TrackId;
                    bestScores = trackBestScores;
                }
            }

            // 5. 判定（多成员库混合逻辑）：
            //    - 无歧义：最高分 > 基础阈值(0.88) 且与次高分差 > 0.08 → 命中
            //    - 有歧义：最高分 >= 高分兜底阈值(0.965) → 仍命中（多为同一人多条目/相似成员）
            if (bestPerson != null && bestScore > HitThreshold &&
                ((bestScore - secondBestScore) > MarginThreshold ||
                 bestScore >= highConfidenceThreshold))
            {
                Log.RecognitionResult(logger, bestPerson.Name, bestScore, bestTrackId, secondBestScore);
                return new PersonRecognition(
                    bestPerson.Id, groupId, bestPerson.Name, bestScore,
                    bestScores.Cloth, bestScores.Head, bestScores.Body, bestScores.Gait);
            }

            Log.RecognitionResult(logger, "stranger", bestScore, bestTrackId, secondBestScore);
            return new PersonRecognition("", groupId, "stranger", bestScore,
                bestScores.Cloth, bestScores.Head, bestScores.Body, bestScores.Gait);
        }
    }
}
