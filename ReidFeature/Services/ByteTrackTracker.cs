using ReidFeature.Helpers;
using SixLabors.ImageSharp;
using System.Runtime.InteropServices;

namespace ReidFeature.Services;

/// <summary>
/// ByteTrack 多目标跟踪器 — 纯 C# 实现
/// 核心: 8 维卡尔曼滤波器 + 匈牙利线性分配 + IoU 级联匹配
/// </summary>
public sealed class ByteTrackTracker
{
    private int _nextTrackId = 1;
    private readonly Dictionary<int, Tracklet> _tracks = [];
    private readonly List<Tracklet> _completedTracks = [];

    private const float MatchThreshold = 0.3f;   // IoU 匹配阈值
    private const int MaxLostFrames = 30;          // 最大丢失帧数
    // 激活阈值：视频按 frameIntervalSeconds 抽帧（默认 5 秒/帧）时，
    // 人物帧间位移大导致 IoU 匹配常失败，HitStreak 难以累积到 3。
    // 保持 3：人物至少连续命中 3 帧才形成有效 Track，过滤单帧误检（单人家庭场景可接受）。
    private const int MinHitStreak = 3;            // 最少连续命中次数（激活阈值）

    /// <summary>
    /// 使用当前帧检测结果更新跟踪器
    /// </summary>
    /// <param name="detections">当前帧检测到的人物 bbox 序列</param>
    /// <returns>跟踪结果列表 (trackId, bbox)</returns>
    public List<(int TrackId, Rectangle Bbox)> Update(ReadOnlySpan<(Rectangle Bbox, float Score)> detections)
    {
        // 1. 所有活跃 Track 做预测
        foreach (var track in _tracks.Values)
        {
            track.KalmanPredict();
            track.Age++;
        }

        // 2. 第一次关联: 高分 Track ↔ 高分检测 (IoU ≥ 0.5)
        //    所有检测统一携带 detections 中的原始索引，确保匹配结果索引与 detections 对齐
        var highScoreDets = new List<(int Idx, Rectangle Bbox, float Score)>();
        for (int i = 0; i < detections.Length; i++)
        {
            if (detections[i].Score >= 0.5f)
            {
                highScoreDets.Add((i, detections[i].Bbox, detections[i].Score));
            }
        }
        var activeTracks = _tracks.Values.Where(t => !t.IsRemoved).ToList();
        var matched1 = LinearAssignment(
            CollectionsMarshal.AsSpan(activeTracks),
            CollectionsMarshal.AsSpan(highScoreDets));

        // 3. 第二次关联: 未匹配 Track ↔ 未匹配高分检测 + 低分检测
        var unmatchedTracks = activeTracks.Where(t => !matched1.MatchedTracks.Contains(t)).ToList();
        var lowScoreDets = new List<(int Idx, Rectangle Bbox, float Score)>();
        for (int i = 0; i < detections.Length; i++)
        {
            if (detections[i].Score < 0.5f)
            {
                lowScoreDets.Add((i, detections[i].Bbox, detections[i].Score));
            }
        }
        var unmatchedDetections = highScoreDets
            .Where(t => !matched1.MatchedDetIndices.Contains(t.Idx))
            .Concat(lowScoreDets)
            .ToList();
        var matched2 = IoUMatching(
            CollectionsMarshal.AsSpan(unmatchedTracks),
            CollectionsMarshal.AsSpan(unmatchedDetections),
            0.5f);

        // 4. 处理未匹配的 Track → 标记为丢失
        foreach (var track in unmatchedTracks.Where(t => !matched2.MatchedTracks.Contains(t)))
        {
            track.LostFrames++;
            // 与原版 ByteTrack 一致：HitStreak 只在命中时递增，丢失时不递减，
            // 激活为一次性闩锁 —— 已激活 Track 在遮挡期仍保持激活，不会被剔除出融合结果
            if (track.LostFrames > MaxLostFrames)
            {
                track.IsRemoved = true;
                if (track.HitStreak >= MinHitStreak)
                {
                    _completedTracks.Add(track);
                }
            }
        }

        // 5. 处理未匹配的检测 → 创建新 Track
        var allMatchedDets = matched1.MatchedDetIndices.Concat(matched2.MatchedDetIndices).ToHashSet();
        for (int i = 0; i < detections.Length; i++)
        {
            if (allMatchedDets.Contains(i))
            {
                continue;
            }

            var (bbox, score) = detections[i];
            var track = new Tracklet(_nextTrackId++, bbox, score);
            _tracks[track.TrackId] = track;
        }

        // 6. 返回当前帧所有激活 Track 的预测结果
        var results = new List<(int, Rectangle)>();
        foreach (var track in _tracks.Values)
        {
            if (!track.IsRemoved && track.HitStreak >= MinHitStreak)
            {
                results.Add((track.TrackId, track.LastBbox));
            }
        }

        return results;
    }

    /// <summary>
    /// 获取指定 Track 的轨迹中心点序列（用于步态标量计算）
    /// </summary>
    public PointF[] GetTrackCenters(int trackId)
    {
        return _tracks.TryGetValue(trackId, out var track)
            ? track.CenterHistory.ToArray()
            : [];
    }

    /// <summary>
    /// 获取所有已完成 Track 的特征包（按存活帧数降序）
    /// 返回后清空已完成队列
    /// </summary>
    /// <returns>已完成 Track 列表（trackId, bbox 序列）</returns>
    public List<(int TrackId, Rectangle FirstBbox, Rectangle LastBbox, PointF[] Centers)> FlushCompletedTracks()
    {
        // 视频流结束时，把已移除的 Track 和仍活跃的 Track 一并返回
        var result = _tracks.Values
            .Where(t => t.IsActive && !t.IsRemoved)
            .Concat(_completedTracks)
            .OrderByDescending(t => t.Age)
            .Select(t => (
                t.TrackId,
                t.FirstBbox,
                t.LastBbox,
                t.CenterHistory.ToArray()))
            .ToList();

        // 清理已返回（已移除或仍活跃）的 Track，避免重复返回
        var flushedIds = _tracks
            .Where(kv => kv.Value.IsRemoved || kv.Value.IsActive)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var id in flushedIds)
        {
            _tracks.Remove(id);
        }

        _completedTracks.Clear();
        return result;
    }

    /// <summary>
    /// 匈牙利算法线性分配 — 基于 IoU 代价矩阵
    /// </summary>
    private static (List<Tracklet> MatchedTracks, HashSet<int> MatchedDetIndices) LinearAssignment(
        ReadOnlySpan<Tracklet> tracks, ReadOnlySpan<(int Idx, Rectangle Bbox, float Score)> detections)
    {
        var matchedTracks = new List<Tracklet>();
        var matchedDetIndices = new HashSet<int>();

        if (tracks.Length == 0 || detections.Length == 0)
        {
            return (matchedTracks, matchedDetIndices);
        }

        // 构建 IoU 代价矩阵
        int n = tracks.Length, m = detections.Length;
        var costMatrix = new float[n, m];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                float iou = ComputeIoU(tracks[i].LastBbox, detections[j].Bbox);
                costMatrix[i, j] = 1f - iou; // 距离 = 1 - IoU
            }
        }

        // 匈牙利匹配
        var assignment = HungarianSolver.Solve(costMatrix);

        for (int i = 0; i < assignment.Length; i++)
        {
            int j = assignment[i];
            if (j < 0 || j >= m)
            {
                continue;
            }

            float iou = 1f - costMatrix[i, j];
            if (iou >= MatchThreshold)
            {
                tracks[i].Update(detections[j].Bbox, detections[j].Score);
                matchedTracks.Add(tracks[i]);
                matchedDetIndices.Add(detections[j].Idx);
            }
        }

        return (matchedTracks, matchedDetIndices);
    }

    /// <summary>
    /// IoU 匹配（第二次关联用更高阈值）
    /// </summary>
    private static (List<Tracklet> MatchedTracks, HashSet<int> MatchedDetIndices) IoUMatching(
        ReadOnlySpan<Tracklet> tracks, ReadOnlySpan<(int Idx, Rectangle Bbox, float Score)> detections, float threshold)
    {
        var matchedTracks = new List<Tracklet>();
        var matchedDetIndices = new HashSet<int>();

        foreach (var track in tracks)
        {
            float bestIoU = threshold;
            int bestIdx = -1;

            for (int j = 0; j < detections.Length; j++)
            {
                if (matchedDetIndices.Contains(j))
                {
                    continue;
                }

                float iou = ComputeIoU(track.LastBbox, detections[j].Bbox);
                if (iou > bestIoU)
                {
                    bestIoU = iou;
                    bestIdx = j;
                }
            }

            if (bestIdx >= 0)
            {
                track.Update(detections[bestIdx].Bbox, detections[bestIdx].Score);
                matchedTracks.Add(track);
                matchedDetIndices.Add(detections[bestIdx].Idx);
            }
        }

        return (matchedTracks, matchedDetIndices);
    }

    private static float ComputeIoU(Rectangle a, Rectangle b)
    {
        int interLeft = Math.Max(a.Left, b.Left);
        int interTop = Math.Max(a.Top, b.Top);
        int interRight = Math.Min(a.Right, b.Right);
        int interBottom = Math.Min(a.Bottom, b.Bottom);

        if (interLeft >= interRight || interTop >= interBottom)
        {
            return 0f;
        }

        int interArea = (interRight - interLeft) * (interBottom - interTop);
        int areaA = a.Width * a.Height;
        int areaB = b.Width * b.Height;
        return (float)interArea / (areaA + areaB - interArea);
    }

    /// <summary>
    /// 重置跟踪器状态
    /// </summary>
    public void Reset()
    {
        _tracks.Clear();
        _completedTracks.Clear();
        _nextTrackId = 1;
    }

    // ════════════════════════════════════════════════════════════
    //  内部类型
    // ════════════════════════════════════════════════════════════

    private sealed class Tracklet
    {
        public int TrackId { get; }
        public Rectangle FirstBbox { get; }
        public Rectangle LastBbox { get; private set; }
        public int HitStreak { get; set; }
        public int Age { get; set; }
        public int LostFrames { get; set; }
        public bool IsRemoved { get; set; }
        public bool IsActive => HitStreak >= MinHitStreak;

        public ReadOnlySpan<PointF> CenterHistory => CollectionsMarshal.AsSpan(_centerHistory);
        private readonly List<PointF> _centerHistory = [];

        private readonly KalmanFilter8 _kalman;

        /// <summary>以首帧检测框初始化 Tracklet。</summary>
        /// <param name="id">Track ID</param>
        /// <param name="bbox">首帧检测框</param>
        /// <param name="score">首帧检测置信度</param>
        public Tracklet(int id, Rectangle bbox, float score)
        {
            TrackId = id;
            FirstBbox = bbox;
            LastBbox = bbox;
            HitStreak = 1;
            Age = 0;
            LostFrames = 0;

            float cx = bbox.X + bbox.Width / 2f;
            float cy = bbox.Y + bbox.Height / 2f;
            _centerHistory.Add(new PointF(cx, cy));
            _kalman = new KalmanFilter8(cx, cy, bbox.Width, bbox.Height);
        }

        /// <summary>用当前帧检测框更新 Tracklet 状态。</summary>
        /// <param name="bbox">当前帧检测框</param>
        /// <param name="score">当前帧检测置信度</param>
        public void Update(Rectangle bbox, float score)
        {
            LastBbox = bbox;
            HitStreak++;
            LostFrames = 0;

            float cx = bbox.X + bbox.Width / 2f;
            float cy = bbox.Y + bbox.Height / 2f;
            _centerHistory.Add(new PointF(cx, cy));
            _kalman.Update([cx, cy, bbox.Width, bbox.Height]);
        }

        /// <summary>执行卡尔曼预测并更新预测框。</summary>
        public void KalmanPredict()
        {
            var pred = _kalman.Predict();
            LastBbox = new Rectangle(
                (int)(pred[0] - pred[2] / 2f),
                (int)(pred[1] - pred[3] / 2f),
                (int)pred[2],
                (int)pred[3]);
        }
    }

    /// <summary>
    /// 8 维卡尔曼滤波器 (cx, cy, w, h, vx, vy, vw, vh)
    /// </summary>
    private sealed class KalmanFilter8
    {
        private float[,] _f;  // 状态转移矩阵 (8x8)
        private float[,] _h;  // 观测矩阵 (4x8)
        private float[,] _p;  // 协方差矩阵 (8x8)
        private float[,] _q;  // 过程噪声 (8x8)
        private float[,] _r;  // 观测噪声 (4x4)
        private float[] _x;   // 状态向量 (8)

        /// <summary>以目标初始状态初始化 8 维卡尔曼滤波器。</summary>
        /// <param name="cx">中心点 X</param>
        /// <param name="cy">中心点 Y</param>
        /// <param name="w">宽度</param>
        /// <param name="h">高度</param>
        public KalmanFilter8(float cx, float cy, float w, float h)
        {
            _x = new float[8] { cx, cy, w, h, 0, 0, 0, 0 };

            _f = new float[8, 8];
            _h = new float[4, 8];
            _p = new float[8, 8];
            _q = new float[8, 8];
            _r = new float[4, 4];

            // F: 恒速模型
            for (int i = 0; i < 8; i++)
            {
                _f[i, i] = 1;
            }
            _f[0, 4] = 1; _f[1, 5] = 1; _f[2, 6] = 1; _f[3, 7] = 1;

            // H: 观测矩阵
            for (int i = 0; i < 4; i++)
            {
                _h[i, i] = 1;
            }

            // P: 初始协方差
            for (int i = 0; i < 8; i++)
            {
                _p[i, i] = 10;
            }

            // Q: 过程噪声
            for (int i = 0; i < 4; i++)
            {
                _q[i, i] = 0.01f;
            }
            for (int i = 4; i < 8; i++)
            {
                _q[i, i] = 0.01f;
            }

            // R: 观测噪声
            for (int i = 0; i < 4; i++)
            {
                _r[i, i] = 0.1f;
            }
        }

        /// <summary>执行状态与协方差预测。</summary>
        /// <returns>预测后的状态向量 (cx, cy, w, h, vx, vy, vw, vh)</returns>
        public float[] Predict()
        {
            // _x = _f @ _x
            var newX = new float[8];
            for (int i = 0; i < 8; i++)
            {
                float sum = 0;
                for (int j = 0; j < 8; j++)
                {
                    sum += _f[i, j] * _x[j];
                }
                newX[i] = sum;
            }
            _x = newX;

            // _p = _f @ _p @ _f^T + _q
            var FP = Multiply(_f, _p);
            var FT = Transpose(_f);
            var FPF = Multiply(FP, FT);
            _p = Add(FPF, _q);

            return _x;
        }

        /// <summary>用观测向量执行卡尔曼更新。</summary>
        /// <param name="z">观测向量 (cx, cy, w, h)</param>
        public void Update(ReadOnlySpan<float> z)
        {
            // y = z - _h @ _x
            var Hx = new float[4];
            for (int i = 0; i < 4; i++)
            {
                float sum = 0;
                for (int j = 0; j < 8; j++)
                {
                    sum += _h[i, j] * _x[j];
                }
                Hx[i] = sum;
            }

            var y = new float[4];
            for (int i = 0; i < 4; i++)
            {
                y[i] = z[i] - Hx[i];
            }

            // S = _h @ _p @ _h^T + _r
            var HP = Multiply(_h, _p);
            var HT = Transpose(_h);
            var HPH = Multiply(HP, HT);
            var S = Add(HPH, _r);

            // K = _p @ _h^T @ inv(S)
            var PHt = Multiply(_p, HT);
            var SInv = Invert4x4(S);
            var K = Multiply(PHt, SInv);

            // _x = _x + K @ y
            var Ky = Multiply(K, y);
            for (int i = 0; i < 8; i++)
            {
                _x[i] += Ky[i];
            }

            // _p = (I - K @ _h) @ _p
            var KH = Multiply(K, _h);
            var I = new float[8, 8];
            for (int i = 0; i < 8; i++)
            {
                I[i, i] = 1;
            }
            var I_KH = Subtract(I, KH);
            _p = Multiply(I_KH, _p);
        }

        // ── 矩阵辅助运算 ──

        private static float[,] Multiply(float[,] a, float[,] b)
        {
            int n = a.GetLength(0), m = a.GetLength(1), p = b.GetLength(1);
            var result = new float[n, p];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < p; j++)
                {
                    float sum = 0;
                    for (int k = 0; k < m; k++)
                    {
                        sum += a[i, k] * b[k, j];
                    }
                    result[i, j] = sum;
                }
            }
            return result;
        }

        private static float[] Multiply(float[,] a, ReadOnlySpan<float> v)
        {
            int n = a.GetLength(0), m = a.GetLength(1);
            var result = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0;
                for (int j = 0; j < m; j++)
                {
                    sum += a[i, j] * v[j];
                }
                result[i] = sum;
            }
            return result;
        }

        private static float[,] Transpose(float[,] a)
        {
            int n = a.GetLength(0), m = a.GetLength(1);
            var result = new float[m, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    result[j, i] = a[i, j];
                }
            }
            return result;
        }

        private static float[,] Add(float[,] a, float[,] b)
        {
            int n = a.GetLength(0), m = a.GetLength(1);
            var result = new float[n, m];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    result[i, j] = a[i, j] + b[i, j];
                }
            }
            return result;
        }

        private static float[,] Subtract(float[,] a, float[,] b)
        {
            int n = a.GetLength(0), m = a.GetLength(1);
            var result = new float[n, m];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    result[i, j] = a[i, j] - b[i, j];
                }
            }
            return result;
        }

        private static float[,] Invert4x4(float[,] m)
        {
            // 使用高斯-约当消元法
            int n = 4;
            var a = new float[n, n];
            var b = new float[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    a[i, j] = m[i, j];
                }
                b[i, i] = 1;
            }

            for (int i = 0; i < n; i++)
            {
                // 选主元
                float maxVal = Math.Abs(a[i, i]);
                int maxRow = i;
                for (int k = i + 1; k < n; k++)
                {
                    if (Math.Abs(a[k, i]) > maxVal)
                    {
                        maxVal = Math.Abs(a[k, i]);
                        maxRow = k;
                    }
                }

                // 交换行
                for (int k = 0; k < n; k++)
                {
                    (a[i, k], a[maxRow, k]) = (a[maxRow, k], a[i, k]);
                    (b[i, k], b[maxRow, k]) = (b[maxRow, k], b[i, k]);
                }

                float diag = a[i, i];
                for (int k = 0; k < n; k++)
                {
                    a[i, k] /= diag;
                    b[i, k] /= diag;
                }

                for (int k = 0; k < n; k++)
                {
                    if (k == i)
                    {
                        continue;
                    }
                    float factor = a[k, i];
                    for (int j = 0; j < n; j++)
                    {
                        a[k, j] -= factor * a[i, j];
                        b[k, j] -= factor * b[i, j];
                    }
                }
            }

            return b;
        }
    }
}
