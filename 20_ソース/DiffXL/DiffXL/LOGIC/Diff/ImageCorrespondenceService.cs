using System;
using System.Collections.Generic;
using System.Linq;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 左右埋め込み画像の類似度行列 + Hungarian 最適 1:1 対応。
    /// </summary>
    public static class ImageCorrespondenceService
    {
        /// <summary>
        /// これ以下の差分比率なら「改訂同一画像」としてペア候補（ダミー割当コスト）。
        /// </summary>
        public const double PairMaxDiffRatio = 0.55;

        /// <summary>
        /// これより大きい差分比率は割当禁止（コスト +∞）。
        /// </summary>
        public const double RejectDiffRatio = 0.85;

        /// <summary>
        /// 数値用の「無限大」コスト（Hungarian 内部）。
        /// </summary>
        private const double InfCost = 1e9;

        /// <summary>
        /// 左右画像を 1:1 最適対応する。結果は
        /// <c>Left?.Anchor.RowStart</c> / <c>Right?.Anchor.RowStart</c> 昇順。
        /// <paramref name="pins"/> はコスト 0 の強制ペア（Hungarian 自由集合から除外）。
        /// </summary>
        public static IList<ImageCorrespondence> Match(
            IList<EmbeddedImage> left,
            IList<EmbeddedImage> right,
            IList<ManualImagePin> pins = null)
        {
            var leftList = (left ?? Array.Empty<EmbeddedImage>())
                .Where(i => i != null)
                .ToList();
            var rightList = (right ?? Array.Empty<EmbeddedImage>())
                .Where(i => i != null)
                .ToList();

            int n = leftList.Count;
            int m = rightList.Count;

            if (n == 0 && m == 0)
            {
                return new List<ImageCorrespondence>();
            }

            if (n == 0)
            {
                return rightList
                    .Select(r => MakeOnly(null, r))
                    .OrderBy(SortKey)
                    .ThenBy(c => c.Right != null ? c.Right.AnchorColumn : int.MaxValue)
                    .ToList();
            }

            if (m == 0)
            {
                return leftList
                    .Select(l => MakeOnly(l, null))
                    .OrderBy(SortKey)
                    .ThenBy(c => c.Left != null ? c.Left.AnchorColumn : int.MaxValue)
                    .ToList();
            }

            var pairedLeft = new bool[n];
            var pairedRight = new bool[m];
            var results = new List<ImageCorrespondence>();

            // 手動ピン: コスト 0 で強制ペアし、Hungarian 自由集合から除外
            ApplyManualPins(leftList, rightList, pins, pairedLeft, pairedRight, results);

            var freeLeft = new List<int>();
            var freeRight = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!pairedLeft[i])
                {
                    freeLeft.Add(i);
                }
            }

            for (int j = 0; j < m; j++)
            {
                if (!pairedRight[j])
                {
                    freeRight.Add(j);
                }
            }

            int fn = freeLeft.Count;
            int fm = freeRight.Count;

            if (fn > 0 && fm > 0)
            {
                var cost = new double[fn, fm];
                var forbidden = new bool[fn, fm];
                for (int i = 0; i < fn; i++)
                {
                    for (int j = 0; j < fm; j++)
                    {
                        double c = ComputeCost(
                            leftList[freeLeft[i]],
                            rightList[freeRight[j]],
                            out bool isForbidden);
                        cost[i, j] = c;
                        forbidden[i, j] = isForbidden;
                    }
                }

                // 正方化: ダミー行/列のコスト = PairMaxDiffRatio
                int dim = Math.Max(fn, fm);
                var square = new double[dim, dim];
                for (int i = 0; i < dim; i++)
                {
                    for (int j = 0; j < dim; j++)
                    {
                        if (i < fn && j < fm)
                        {
                            square[i, j] = cost[i, j];
                        }
                        else
                        {
                            square[i, j] = PairMaxDiffRatio;
                        }
                    }
                }

                int[] assignment = HungarianMinCost(square);

                for (int i = 0; i < dim; i++)
                {
                    int j = assignment[i];
                    if (i >= fn || j >= fm)
                    {
                        continue;
                    }

                    if (forbidden[i, j] || cost[i, j] >= InfCost * 0.5)
                    {
                        continue;
                    }

                    if (cost[i, j] > PairMaxDiffRatio)
                    {
                        continue;
                    }

                    int liIdx = freeLeft[i];
                    int riIdx = freeRight[j];
                    EmbeddedImage li = leftList[liIdx];
                    EmbeddedImage ri = rightList[riIdx];
                    bool exact = IsExactHash(li, ri);
                    results.Add(new ImageCorrespondence
                    {
                        Left = li,
                        Right = ri,
                        DiffRatio = exact ? 0.0 : cost[i, j],
                        IsExactHashMatch = exact
                    });
                    pairedLeft[liIdx] = true;
                    pairedRight[riIdx] = true;
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (!pairedLeft[i])
                {
                    results.Add(MakeOnly(leftList[i], null));
                }
            }

            for (int j = 0; j < m; j++)
            {
                if (!pairedRight[j])
                {
                    results.Add(MakeOnly(null, rightList[j]));
                }
            }

            return results
                .OrderBy(SortKey)
                .ThenBy(c =>
                {
                    int lc = c.Left != null ? c.Left.AnchorColumn : int.MaxValue;
                    int rc = c.Right != null ? c.Right.AnchorColumn : int.MaxValue;
                    return Math.Min(lc, rc);
                })
                .ToList();
        }

        /// <summary>
        /// 手動ピンをコスト 0 の強制ペアとして確定する。
        /// </summary>
        private static void ApplyManualPins(
            List<EmbeddedImage> leftList,
            List<EmbeddedImage> rightList,
            IList<ManualImagePin> pins,
            bool[] pairedLeft,
            bool[] pairedRight,
            List<ImageCorrespondence> results)
        {
            if (pins == null || pins.Count == 0)
            {
                return;
            }

            foreach (ManualImagePin pin in pins)
            {
                if (pin == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(pin.LeftImageHash) || string.IsNullOrEmpty(pin.RightImageHash))
                {
                    continue;
                }

                int li = FindUnpairedByHash(leftList, pairedLeft, pin.LeftImageHash);
                int ri = FindUnpairedByHash(rightList, pairedRight, pin.RightImageHash);
                if (li < 0 || ri < 0)
                {
                    continue;
                }

                EmbeddedImage left = leftList[li];
                EmbeddedImage right = rightList[ri];
                bool exact = IsExactHash(left, right);
                results.Add(new ImageCorrespondence
                {
                    Left = left,
                    Right = right,
                    DiffRatio = 0.0,
                    IsExactHashMatch = exact
                });
                pairedLeft[li] = true;
                pairedRight[ri] = true;
            }
        }

        private static int FindUnpairedByHash(
            List<EmbeddedImage> list,
            bool[] paired,
            string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return -1;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (paired[i])
                {
                    continue;
                }

                EmbeddedImage img = list[i];
                if (img == null || string.IsNullOrEmpty(img.ContentHash))
                {
                    continue;
                }

                if (string.Equals(img.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static double ComputeCost(EmbeddedImage left, EmbeddedImage right, out bool forbidden)
        {
            forbidden = false;

            if (IsExactHash(left, right))
            {
                return 0.0;
            }

            string lp = left != null ? left.ExtractedPath : null;
            string rp = right != null ? right.ExtractedPath : null;
            double? ratio = ImageDiffService.TryGetDiffRatio(lp, rp);
            if (ratio == null)
            {
                // 読み込み失敗 → 1.0（有限だが PairMaxDiff 超のため最終的にペアにならない）
                return 1.0;
            }

            if (ratio.Value > RejectDiffRatio)
            {
                forbidden = true;
                return InfCost;
            }

            return ratio.Value;
        }

        private static bool IsExactHash(EmbeddedImage left, EmbeddedImage right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(left.ContentHash) || string.IsNullOrEmpty(right.ContentHash))
            {
                return false;
            }

            return string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
        }

        private static ImageCorrespondence MakeOnly(EmbeddedImage left, EmbeddedImage right)
        {
            return new ImageCorrespondence
            {
                Left = left,
                Right = right,
                DiffRatio = -1.0,
                IsExactHashMatch = false
            };
        }

        private static int SortKey(ImageCorrespondence c)
        {
            int leftRow = int.MaxValue;
            if (c.Left != null)
            {
                if (c.Left.Anchor != null)
                {
                    leftRow = c.Left.Anchor.RowStart;
                }
                else if (c.Left.AnchorRow > 0)
                {
                    leftRow = c.Left.AnchorRow;
                }
            }

            int rightRow = int.MaxValue;
            if (c.Right != null)
            {
                if (c.Right.Anchor != null)
                {
                    rightRow = c.Right.Anchor.RowStart;
                }
                else if (c.Right.AnchorRow > 0)
                {
                    rightRow = c.Right.AnchorRow;
                }
            }

            // ペアは左行優先、片側は存在する側の行
            if (c.IsPaired)
            {
                return leftRow != int.MaxValue ? leftRow : rightRow;
            }

            if (c.IsLeftOnly)
            {
                return leftRow;
            }

            return rightRow;
        }

        /// <summary>
        /// Kuhn–Munkres（Hungarian）最小コスト完全割当。
        /// 戻り値 assignment[row] = col。
        /// </summary>
        private static int[] HungarianMinCost(double[,] a)
        {
            int n = a.GetLength(0);
            if (n == 0)
            {
                return Array.Empty<int>();
            }

            // 1-based 作業配列（定番実装に合わせる）
            var u = new double[n + 1];
            var v = new double[n + 1];
            var p = new int[n + 1];
            var way = new int[n + 1];

            for (int i = 1; i <= n; i++)
            {
                p[0] = i;
                int j0 = 0;
                var minv = new double[n + 1];
                var used = new bool[n + 1];
                for (int j = 0; j <= n; j++)
                {
                    minv[j] = InfCost * 10;
                    used[j] = false;
                }

                do
                {
                    used[j0] = true;
                    int i0 = p[j0];
                    double delta = InfCost * 10;
                    int j1 = 0;
                    for (int j = 1; j <= n; j++)
                    {
                        if (used[j])
                        {
                            continue;
                        }

                        double cur = a[i0 - 1, j - 1] - u[i0] - v[j];
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

                    for (int j = 0; j <= n; j++)
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
                }
                while (p[j0] != 0);

                do
                {
                    int j1 = way[j0];
                    p[j0] = p[j1];
                    j0 = j1;
                }
                while (j0 != 0);
            }

            // p[col] = row (1-based) → assignment[row0] = col0
            var assignment = new int[n];
            for (int j = 1; j <= n; j++)
            {
                if (p[j] > 0)
                {
                    assignment[p[j] - 1] = j - 1;
                }
            }

            return assignment;
        }
    }
}
