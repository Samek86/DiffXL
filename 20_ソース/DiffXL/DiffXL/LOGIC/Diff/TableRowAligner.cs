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
        /// </summary>
        private const double MatchThreshold = 0.5;

        /// <summary>
        /// SkipLeft / SkipRight 1 回あたりのコスト。
        /// </summary>
        private const double SkipCost = 0.4;

        /// <summary>
        /// 左右の行系列をアラインし、Match / SkipLeft / SkipRight のステップ列を返す。
        /// 行キー完全一致は類似度 1。不一致時はセル Text の一致割合（最大列数で正規化）。
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

            return SequenceAligner.Align(
                n,
                m,
                (i, j) => RowSimilarity(left[i], right[j], leftKeys[i], rightKeys[j]),
                MatchThreshold,
                SkipCost);
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
        /// 2 行の類似度（0..1）。キー一致は 1。否则はセル Text 一致数 / max(列数)。
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
            int maxLen = Math.Max(leftLen, rightLen);
            if (maxLen == 0)
            {
                return 1.0;
            }

            int minLen = Math.Min(leftLen, rightLen);
            int equal = 0;
            for (int c = 0; c < minLen; c++)
            {
                string lt = GetText(leftRow[c]);
                string rt = GetText(rightRow[c]);
                if (string.Equals(lt, rt, StringComparison.Ordinal))
                {
                    equal++;
                }
            }

            return (double)equal / maxLen;
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
