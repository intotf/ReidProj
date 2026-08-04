namespace ReidFeature.Helpers;

/// <summary>
/// 匈牙利算法求解器（最小化指派问题，O(n³)）
/// </summary>
internal static class HungarianSolver
{
    /// <summary>
    /// 求解最小化指派问题
    /// </summary>
    /// <param name="cost">n×m 代价矩阵</param>
    /// <returns>长度为 n 的数组，result[i] = 分配给第 i 行的列索引，无指派则为 -1</returns>
    public static int[] Solve(float[,] cost)
    {
        int n = cost.GetLength(0);
        int m = cost.GetLength(1);
        int size = Math.Max(n, m);

        // 扩展为方阵
        var a = new float[size, size];
        float maxCost = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                a[i, j] = cost[i, j];
                if (cost[i, j] > maxCost)
                {
                    maxCost = cost[i, j];
                }
            }
        }
        // 填充扩展行/列为大值（最小化问题中避免匹配到虚拟行列）
        float big = maxCost + 1;
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                if (i >= n || j >= m)
                {
                    a[i, j] = big;
                }
            }
        }

        // 标准 Hungarian 算法 (Munkres)
        var u = new float[size];
        var v = new float[size];
        var p = new int[size];
        var way = new int[size];

        for (int i = 0; i < size; i++)
        {
            p[0] = i;
            int j0 = 0;
            var minv = new float[size];
            var used = new bool[size];

            for (int j = 0; j < size; j++)
            {
                minv[j] = float.MaxValue;
                used[j] = false;
            }

            do
            {
                used[j0] = true;
                int i0 = p[j0];
                float delta = float.MaxValue;
                int j1 = 0;

                for (int j = 1; j < size; j++)
                {
                    if (!used[j])
                    {
                        float cur = a[i0, j] - u[i0] - v[j];
                        if (cur < minv[j])
                        {
                            minv[j] = cur;
                            way[j] = j0;
                        }
                        if (minv[j] < delta)
                        {
                            delta = minv[j];
                            j1 = j;
                        }
                    }
                }

                for (int j = 0; j < size; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minv[j] -= delta;
                    }
                }

                j0 = j1;
            } while (p[j0] != 0);

            // 增广
            do
            {
                int j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        // 提取结果
        var result = new int[n];
        Array.Fill(result, -1);
        for (int j = 1; j < size; j++)
        {
            if (p[j] < n && j < m)
            {
                result[p[j]] = j;
            }
        }

        return result;
    }
}
