using System;
using System.Collections.Generic;
using System.Globalization;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 図形系列の対応と比較。画像と同様に SequenceAligner で Match / Skip し、
    /// 位置はコストに含めない。内容は Text / Kind / ContentHash を優先する。
    /// </summary>
    public static class ShapeCompareService
    {
        /// <summary>
        /// Match に必要な最小類似度。
        /// </summary>
        private const double MatchThreshold = 0.7;

        /// <summary>
        /// SkipLeft / SkipRight 1 回あたりのコスト。
        /// </summary>
        private const double SkipCost = 0.4;

        /// <summary>
        /// 左右の図形一覧を出現順系列として比較し、差分 DiffItem を返す。
        /// SkipLeft → ShapeOnlyLeft、SkipRight → ShapeOnlyRight、
        /// Match で内容差 → Shape。同一内容の Match は出力しない。
        /// </summary>
        /// <param name="leftShapes">左シートの図形（出現順）</param>
        /// <param name="rightShapes">右シートの図形（出現順）</param>
        /// <param name="pair">シート対応</param>
        /// <returns>差分一覧（空でも null ではない）</returns>
        public static IList<DiffItem> Compare(
            IList<ShapeContent> leftShapes,
            IList<ShapeContent> rightShapes,
            SheetPair pair)
        {
            var items = new List<DiffItem>();

            IList<ShapeContent> left = leftShapes ?? Array.Empty<ShapeContent>();
            IList<ShapeContent> right = rightShapes ?? Array.Empty<ShapeContent>();

            string sheetL = pair != null ? pair.LeftSheet : null;
            string sheetR = pair != null ? pair.RightSheet : null;

            IList<AlignStep> steps = SequenceAligner.Align(
                left.Count,
                right.Count,
                (i, j) => ShapeSimilarity(left[i], right[j]),
                MatchThreshold,
                SkipCost);

            foreach (AlignStep step in steps)
            {
                if (step.Op == AlignOp.Match)
                {
                    ShapeContent ls = left[step.LeftIndex];
                    ShapeContent rs = right[step.RightIndex];
                    if (IsSameContent(ls, rs))
                    {
                        continue;
                    }

                    items.Add(CreateShapeDiff(ls, rs, sheetL, sheetR));
                }
                else if (step.Op == AlignOp.SkipLeft)
                {
                    ShapeContent ls = left[step.LeftIndex];
                    items.Add(CreateOnlyLeft(ls, sheetL, sheetR));
                }
                else if (step.Op == AlignOp.SkipRight)
                {
                    ShapeContent rs = right[step.RightIndex];
                    items.Add(CreateOnlyRight(rs, sheetL, sheetR));
                }
            }

            return items;
        }

        /// <summary>
        /// 2 図形の類似度（0..1）。アンカー位置は使わない。
        /// </summary>
        private static double ShapeSimilarity(ShapeContent left, ShapeContent right)
        {
            if (left == null || right == null)
            {
                return 0.0;
            }

            // ContentHash 一致は同一内容
            if (!string.IsNullOrEmpty(left.ContentHash)
                && !string.IsNullOrEmpty(right.ContentHash)
                && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            string lt = left.Text ?? string.Empty;
            string rt = right.Text ?? string.Empty;
            bool hasTextL = lt.Length > 0;
            bool hasTextR = rt.Length > 0;
            bool kindSame = string.Equals(
                left.Kind ?? string.Empty,
                right.Kind ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            if (hasTextL && hasTextR)
            {
                bool textSame = string.Equals(lt, rt, StringComparison.Ordinal);
                if (textSame && kindSame)
                {
                    return 1.0;
                }

                if (textSame)
                {
                    // テキスト同一・種別違いでも強く対応
                    return 0.9;
                }

                // 種別一致なら内容差があっても Match し、Shape Diff に落とす
                double textSim = TextSimilarity(lt, rt);
                if (kindSame)
                {
                    return 0.8 + 0.2 * textSim;
                }

                return 0.35 * textSim;
            }

            // 片方だけテキスト → ほぼ別物（系列スキップを優先）
            if (hasTextL != hasTextR)
            {
                return kindSame ? 0.4 : 0.1;
            }

            // 双方テキストなし: Kind 一致なら中程度（ハッシュ差でも対応候補）
            if (kindSame)
            {
                return 0.75;
            }

            return 0.0;
        }

        /// <summary>
        /// 簡易テキスト類似度（一致 1、包含 0.6、それ以外は文字一致率）。
        /// </summary>
        private static double TextSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
            {
                return 1.0;
            }

            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return 0.0;
            }

            if (string.Equals(a, b, StringComparison.Ordinal))
            {
                return 1.0;
            }

            if (a.IndexOf(b, StringComparison.Ordinal) >= 0
                || b.IndexOf(a, StringComparison.Ordinal) >= 0)
            {
                int min = Math.Min(a.Length, b.Length);
                int max = Math.Max(a.Length, b.Length);
                return max == 0 ? 1.0 : 0.6 + 0.3 * (min / (double)max);
            }

            // 前方一致長 / max
            int n = Math.Min(a.Length, b.Length);
            int same = 0;
            for (int i = 0; i < n; i++)
            {
                if (a[i] == b[i])
                {
                    same++;
                }
                else
                {
                    break;
                }
            }

            int maxLen = Math.Max(a.Length, b.Length);
            return maxLen == 0 ? 1.0 : same / (double)maxLen;
        }

        /// <summary>
        /// 内容が同一とみなせるか（ハッシュまたは Text+Kind）。
        /// </summary>
        private static bool IsSameContent(ShapeContent left, ShapeContent right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(left.ContentHash)
                && !string.IsNullOrEmpty(right.ContentHash)
                && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            bool kindSame = string.Equals(
                left.Kind ?? string.Empty,
                right.Kind ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            bool textSame = string.Equals(
                left.Text ?? string.Empty,
                right.Text ?? string.Empty,
                StringComparison.Ordinal);

            return kindSame && textSame
                && !string.IsNullOrEmpty(left.Text);
        }

        /// <summary>
        /// 対応図形の内容差分。
        /// </summary>
        private static DiffItem CreateShapeDiff(
            ShapeContent left,
            ShapeContent right,
            string sheetL,
            string sheetR)
        {
            return new DiffItem
            {
                Kind = DiffKind.Shape,
                SheetLeft = sheetL,
                SheetRight = sheetR,
                AddressLeft = FormatAnchor(left != null ? left.Anchor : null),
                AddressRight = FormatAnchor(right != null ? right.Anchor : null),
                LeftImagePath = left != null ? left.RasterPath : null,
                RightImagePath = right != null ? right.RasterPath : null,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "図形差分 [{0}/{1}] 「{2}」→「{3}」",
                    left != null ? (left.Kind ?? "?") : "?",
                    right != null ? (right.Kind ?? "?") : "?",
                    Truncate(left != null ? left.Text : null, 40),
                    Truncate(right != null ? right.Text : null, 40)),
                OrderHint = PickOrderHint(left, right)
            };
        }

        /// <summary>
        /// 左のみ図形。
        /// </summary>
        private static DiffItem CreateOnlyLeft(
            ShapeContent left,
            string sheetL,
            string sheetR)
        {
            return new DiffItem
            {
                Kind = DiffKind.ShapeOnlyLeft,
                SheetLeft = sheetL,
                SheetRight = sheetR,
                AddressLeft = FormatAnchor(left != null ? left.Anchor : null),
                AddressRight = null,
                LeftImagePath = left != null ? left.RasterPath : null,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "図形（左のみ） [{0}] 「{1}」",
                    left != null ? (left.Kind ?? "?") : "?",
                    Truncate(left != null ? left.Text : null, 40)),
                OrderHint = PickOrderHint(left, null)
            };
        }

        /// <summary>
        /// 右のみ図形。
        /// </summary>
        private static DiffItem CreateOnlyRight(
            ShapeContent right,
            string sheetL,
            string sheetR)
        {
            return new DiffItem
            {
                Kind = DiffKind.ShapeOnlyRight,
                SheetLeft = sheetL,
                SheetRight = sheetR,
                AddressLeft = null,
                AddressRight = FormatAnchor(right != null ? right.Anchor : null),
                RightImagePath = right != null ? right.RasterPath : null,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "図形（右のみ） [{0}] 「{1}」",
                    right != null ? (right.Kind ?? "?") : "?",
                    Truncate(right != null ? right.Text : null, 40)),
                OrderHint = PickOrderHint(null, right)
            };
        }

        /// <summary>
        /// アンカーを簡易アドレス文字列に。
        /// </summary>
        private static string FormatAnchor(AnchorRect anchor)
        {
            if (anchor == null || !anchor.IsValid)
            {
                return null;
            }

            return anchor.ToString();
        }

        /// <summary>
        /// OrderHint（行優先、無ければ OrderIndex）。
        /// </summary>
        private static double PickOrderHint(ShapeContent left, ShapeContent right)
        {
            if (left != null && left.Anchor != null && left.Anchor.RowStart > 0)
            {
                return left.Anchor.RowStart;
            }

            if (right != null && right.Anchor != null && right.Anchor.RowStart > 0)
            {
                return right.Anchor.RowStart;
            }

            if (left != null)
            {
                return left.OrderIndex + 1;
            }

            if (right != null)
            {
                return right.OrderIndex + 1;
            }

            return 0;
        }

        /// <summary>
        /// 要約用に文字列を切り詰める。
        /// </summary>
        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            if (s.Length <= maxLen)
            {
                return s;
            }

            return s.Substring(0, maxLen) + "…";
        }
    }
}
