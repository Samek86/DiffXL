using System;
using System.Collections.Generic;
using System.Text;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// テーブル行の系列アラインメント（行 LCS）。
    /// 行キーは各セル Text をタブ結合したもの。背景色は行対応には使わず、セル変更検出側で扱う。
    /// </summary>
    public static class TableRowAligner
    {
        /// <summary>
        /// 行対応の類似度しきい値（これ未満は Match 不可）。
        /// SequenceAligner は sim &gt;= threshold。ちょうど 0.5（2 列で 1 セル一致）を弾くため半開にする。
        /// </summary>
        private const double MatchThreshold = 0.5 + 1e-12;

        /// <summary>
        /// SkipLeft / SkipRight 1 回あたりのコスト。
        /// </summary>
        private const double SkipCost = 0.4;

        /// <summary>
        /// 左右の行系列をアラインし、Match / SkipLeft / SkipRight のステップ列を返す。
        /// 行キー完全一致は類似度 1。不一致時は非空セル一致割合（双方空欄は除外、比較 2 未満は 0）。
        /// </summary>
        /// <param name="leftRows">左テーブルの行一覧</param>
        /// <param name="rightRows">右テーブルの行一覧</param>
        /// <returns>先頭から末尾への AlignStep 列</returns>
        public static IList<AlignStep> AlignRows(
            IList<IList<CellContent>> leftRows,
            IList<IList<CellContent>> rightRows)
        {
            IList<IList<CellContent>> left = leftRows ?? Array.Empty<IList<CellContent>>();
            IList<IList<CellContent>> right = rightRows ?? Array.Empty<IList<CellContent>>();

            int n = left.Count;
            int m = right.Count;

            // 行キーを先に計算して比較を高速化
            var leftKeys = new string[n];
            for (int i = 0; i < n; i++)
            {
                leftKeys[i] = MakeRowKey(left[i]);
            }

            var rightKeys = new string[m];
            for (int j = 0; j < m; j++)
            {
                rightKeys[j] = MakeRowKey(right[j]);
            }

            // 同数かつ全行キー一致なら DP 不要（長大・ほぼ同一表の高速パス）
            if (n == m && n > 0)
            {
                bool allEqual = true;
                for (int i = 0; i < n; i++)
                {
                    if (!string.Equals(leftKeys[i], rightKeys[i], StringComparison.Ordinal))
                    {
                        allEqual = false;
                        break;
                    }
                }

                if (allEqual)
                {
                    var fast = new List<AlignStep>(n);
                    for (int i = 0; i < n; i++)
                    {
                        fast.Add(new AlignStep
                        {
                            Op = AlignOp.Match,
                            LeftIndex = i,
                            RightIndex = i
                        });
                    }

                    return fast;
                }
            }

            return SequenceAligner.Align(
                n,
                m,
                (i, j) => RowSimilarity(left[i], right[j], leftKeys[i], rightKeys[j]),
                MatchThreshold,
                SkipCost);
        }

        /// <summary>
        /// 2 テーブルのソフト類似度（0..1）。
        /// 行 LCS（AlignRows）で Match した行数 / max(左行数, 右行数)。
        /// 完全一致 Jaccard が低くても、行 ID が揃いセル一部だけ違う表を対応付けられる。
        /// </summary>
        public static double SoftTableSimilarity(TableBlock left, TableBlock right)
        {
            IList<IList<CellContent>> leftRows =
                left != null && left.Rows != null ? left.Rows : Array.Empty<IList<CellContent>>();
            IList<IList<CellContent>> rightRows =
                right != null && right.Rows != null ? right.Rows : Array.Empty<IList<CellContent>>();

            if (leftRows.Count == 0 && rightRows.Count == 0)
            {
                return 1.0;
            }

            if (leftRows.Count == 0 || rightRows.Count == 0)
            {
                return 0.0;
            }

            IList<AlignStep> steps = AlignRows(leftRows, rightRows);
            int match = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] != null && steps[i].Op == AlignOp.Match)
                {
                    match++;
                }
            }

            int denom = Math.Max(leftRows.Count, rightRows.Count);
            return denom <= 0 ? 1.0 : (double)match / denom;
        }

        /// <summary>
        /// 行キー = 各セル Text をタブで結合（null は空文字）。背景は含めない。
        /// </summary>
        internal static string MakeRowKey(IList<CellContent> row)
        {
            if (row == null || row.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0)
                {
                    sb.Append('\t');
                }

                CellContent cell = row[c];
                sb.Append(cell != null && cell.Text != null ? cell.Text : string.Empty);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 2 行の類似度（0..1）。キー一致は 1。
        /// 双方空欄は比較に入れない。非空比較が 2 未満なら 0。それ以外は一致非空 / 比較数。
        /// </summary>
        private static double RowSimilarity(
            IList<CellContent> leftRow,
            IList<CellContent> rightRow,
            string leftKey,
            string rightKey)
        {
            if (string.Equals(leftKey, rightKey, StringComparison.Ordinal))
            {
                return 1.0;
            }

            int leftLen = leftRow != null ? leftRow.Count : 0;
            int rightLen = rightRow != null ? rightRow.Count : 0;
            if (leftLen == 0 && rightLen == 0)
            {
                return 1.0;
            }

            int compared = 0;
            int equal = 0;
            int maxCols = Math.Max(leftLen, rightLen);
            for (int c = 0; c < maxCols; c++)
            {
                string lt = c < leftLen ? GetText(leftRow[c]) : string.Empty;
                string rt = c < rightLen ? GetText(rightRow[c]) : string.Empty;
                bool le = string.IsNullOrEmpty(lt);
                bool re = string.IsNullOrEmpty(rt);
                if (le && re)
                {
                    continue;
                }

                compared++;
                if (!le && !re && string.Equals(lt, rt, StringComparison.Ordinal))
                {
                    equal++;
                }
            }

            if (compared < 2)
            {
                return 0;
            }

            return (double)equal / compared;
        }

        /// <summary>
        /// セルの Text（null 安全）。
        /// </summary>
        private static string GetText(CellContent cell)
        {
            return cell != null && cell.Text != null ? cell.Text : string.Empty;
        }
    }
}
