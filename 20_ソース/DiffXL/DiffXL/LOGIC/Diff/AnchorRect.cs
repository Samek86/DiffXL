using System;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 画像などが占有するセル矩形（1 始まり inclusive）。
    /// OOXML drawing の from / to から構築する。
    /// </summary>
    public sealed class AnchorRect
    {
        /// <summary>
        /// 開始行（1 始まり inclusive）。不明時は 0。
        /// </summary>
        public int RowStart { get; set; }

        /// <summary>
        /// 終了行（1 始まり inclusive。&gt;= RowStart）。不明時は 0。
        /// </summary>
        public int RowEnd { get; set; }

        /// <summary>
        /// 開始列（1 始まり inclusive）。不明時は 0。
        /// </summary>
        public int ColStart { get; set; }

        /// <summary>
        /// 終了列（1 始まり inclusive。&gt;= ColStart）。不明時は 0。
        /// </summary>
        public int ColEnd { get; set; }

        /// <summary>
        /// 占有行数（最低 1）。
        /// </summary>
        public int RowSpan
        {
            get { return Math.Max(1, RowEnd - RowStart + 1); }
        }

        /// <summary>
        /// 占有列数（最低 1）。
        /// </summary>
        public int ColSpan
        {
            get { return Math.Max(1, ColEnd - ColStart + 1); }
        }

        /// <summary>
        /// 有効なセルアンカーかどうか（開始行が 1 以上）。
        /// </summary>
        public bool IsValid
        {
            get { return RowStart >= 1 && ColStart >= 1 && RowEnd >= RowStart && ColEnd >= ColStart; }
        }

        /// <summary>
        /// from/to の 0 始まりインデックスから inclusive な 1 始まり矩形を作る。
        /// to が無い場合（oneCell）は Start=End。負値は無効（0）として扱う。
        /// </summary>
        /// <param name="fromRow0">from/row（0-based）</param>
        /// <param name="fromCol0">from/col（0-based）</param>
        /// <param name="toRow0">to/row（0-based）。未指定は -1</param>
        /// <param name="toCol0">to/col（0-based）。未指定は -1</param>
        /// <returns>正規化済み矩形。from 無効時は null</returns>
        public static AnchorRect FromZeroBased(int fromRow0, int fromCol0, int toRow0, int toCol0)
        {
            if (fromRow0 < 0 || fromCol0 < 0)
            {
                return null;
            }

            int rowStart = fromRow0 + 1;
            int colStart = fromCol0 + 1;
            int rowEnd;
            int colEnd;
            if (toRow0 >= 0 && toCol0 >= 0)
            {
                // OOXML の to は終了セル 0-based を inclusive 扱い。from より小さい場合は max で正規化。
                rowEnd = Math.Max(fromRow0, toRow0) + 1;
                colEnd = Math.Max(fromCol0, toCol0) + 1;
            }
            else if (toRow0 >= 0)
            {
                rowEnd = Math.Max(fromRow0, toRow0) + 1;
                colEnd = colStart;
            }
            else if (toCol0 >= 0)
            {
                rowEnd = rowStart;
                colEnd = Math.Max(fromCol0, toCol0) + 1;
            }
            else
            {
                // oneCellAnchor 等: Start = End
                rowEnd = rowStart;
                colEnd = colStart;
            }

            return new AnchorRect
            {
                RowStart = rowStart,
                RowEnd = rowEnd,
                ColStart = colStart,
                ColEnd = colEnd
            };
        }

        /// <summary>
        /// 浅いコピー。
        /// </summary>
        public AnchorRect Clone()
        {
            return new AnchorRect
            {
                RowStart = RowStart,
                RowEnd = RowEnd,
                ColStart = ColStart,
                ColEnd = ColEnd
            };
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "R{0}-{1}:C{2}-{3}",
                RowStart,
                RowEnd,
                ColStart,
                ColEnd);
        }
    }
}
