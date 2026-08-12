using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 統一内容ストリーム上のブロック種別（シート上の出現順で並べる）。
    /// </summary>
    public enum ContentBlockKind
    {
        /// <summary>テーブル外セルの 1 行分。</summary>
        LooseRow,

        /// <summary>ボーダー検出テーブル。</summary>
        Table,

        /// <summary>埋め込み画像。</summary>
        Image,

        /// <summary>図形。</summary>
        Shape
    }

    /// <summary>
    /// シート内容を「1 本のストリーム」にした 1 ブロック。
    /// </summary>
    public sealed class ContentStreamBlock
    {
        /// <summary>種別。</summary>
        public ContentBlockKind Kind { get; set; }

        /// <summary>並び用の行（1 始まり。不明時は 0）。</summary>
        public int Row { get; set; }

        /// <summary>並び用の列（1 始まり。不明時は 0）。</summary>
        public int Column { get; set; }

        /// <summary>MiniMap / ジャンプ用の順序ヒント。</summary>
        public double OrderHint { get; set; }

        /// <summary>安定 ID（アライン・デバッグ用）。</summary>
        public string Id { get; set; }

        /// <summary>比較用署名（種別＋内容）。</summary>
        public string Signature { get; set; }

        /// <summary>LooseRow 時のセル列。</summary>
        public IList<CellContent> Cells { get; set; }

        /// <summary>Table 時。</summary>
        public TableBlock Table { get; set; }

        /// <summary>Image 時。</summary>
        public EmbeddedImage Image { get; set; }

        /// <summary>Shape 時。</summary>
        public ShapeContent Shape { get; set; }
    }

    /// <summary>
    /// 左右アライン済みの 1 行（左右どちらか欠落可）。
    /// </summary>
    public sealed class ContentStreamPair
    {
        /// <summary>アライン操作。</summary>
        public AlignOp Op { get; set; }

        /// <summary>左ブロック（SkipRight 時は null）。</summary>
        public ContentStreamBlock Left { get; set; }

        /// <summary>右ブロック（SkipLeft 時は null）。</summary>
        public ContentStreamBlock Right { get; set; }

        /// <summary>ジャンプ用の代表 OrderHint。</summary>
        public double OrderHint
        {
            get
            {
                if (Left != null && Left.OrderHint > 0)
                {
                    return Left.OrderHint;
                }

                if (Right != null && Right.OrderHint > 0)
                {
                    return Right.OrderHint;
                }

                return 0;
            }
        }
    }

    /// <summary>
    /// シート内容をドキュメント順の統一ストリームに組み立て、左右をアラインする。
    /// </summary>
    public static class ContentStreamBuilder
    {
        /// <summary>
        /// ブロック一致の類似度しきい値。
        /// </summary>
        public const double MatchThreshold = 0.55;

        /// <summary>
        /// Skip コスト。
        /// </summary>
        public const double SkipCost = 0.4;

        /// <summary>
        /// 1 シートから出現順ストリームを構築する。
        /// </summary>
        /// <param name="sheet">シート（null 可）</param>
        /// <returns>行→列順のブロック列</returns>
        public static IList<ContentStreamBlock> Build(SheetContent sheet)
        {
            var list = new List<ContentStreamBlock>();
            if (sheet == null)
            {
                return list;
            }

            if (sheet.Tables != null)
            {
                foreach (TableBlock table in sheet.Tables)
                {
                    if (table == null)
                    {
                        continue;
                    }

                    list.Add(new ContentStreamBlock
                    {
                        Kind = ContentBlockKind.Table,
                        Row = table.RowStart,
                        Column = table.ColStart,
                        OrderHint = OrderKey(table.RowStart, table.ColStart),
                        Id = "T:" + (table.Id ?? string.Empty),
                        Signature = "T|" + TableSignature(table),
                        Table = table
                    });
                }
            }

            if (sheet.Images != null)
            {
                int imgIndex = 0;
                foreach (EmbeddedImage image in sheet.Images)
                {
                    if (image == null)
                    {
                        continue;
                    }

                    int row = image.Anchor != null ? image.Anchor.RowStart : image.AnchorRow;
                    int col = image.Anchor != null ? image.Anchor.ColStart : image.AnchorColumn;
                    list.Add(new ContentStreamBlock
                    {
                        Kind = ContentBlockKind.Image,
                        Row = row,
                        Column = col,
                        OrderHint = OrderKey(row, col),
                        Id = "I:" + imgIndex + ":" + (image.ContentHash ?? image.FileName ?? string.Empty),
                        Signature = "I|" + (image.ContentHash ?? string.Empty) + "|"
                            + (image.FileName ?? string.Empty),
                        Image = image
                    });
                    imgIndex++;
                }
            }

            if (sheet.Shapes != null)
            {
                foreach (ShapeContent shape in sheet.Shapes)
                {
                    if (shape == null)
                    {
                        continue;
                    }

                    int row = shape.Anchor != null ? shape.Anchor.RowStart : 0;
                    int col = shape.Anchor != null ? shape.Anchor.ColStart : 0;
                    list.Add(new ContentStreamBlock
                    {
                        Kind = ContentBlockKind.Shape,
                        Row = row,
                        Column = col,
                        OrderHint = OrderKey(row, col),
                        Id = "S:" + (shape.Id ?? shape.OrderIndex.ToString(CultureInfo.InvariantCulture)),
                        Signature = "S|" + (shape.ContentHash ?? string.Empty) + "|"
                            + (shape.Text ?? string.Empty) + "|" + (shape.Kind ?? string.Empty),
                        Shape = shape
                    });
                }
            }

            // テーブル外セルは行ごとに 1 ブロック
            if (sheet.LooseCells != null && sheet.LooseCells.Count > 0)
            {
                foreach (IGrouping<int, CellContent> group in sheet.LooseCells
                    .Where(c => c != null && !string.IsNullOrEmpty(c.Text))
                    .GroupBy(c => c.Row > 0 ? c.Row : 0)
                    .OrderBy(g => g.Key))
                {
                    List<CellContent> cells = group.OrderBy(c => c.Column).ToList();
                    if (cells.Count == 0)
                    {
                        continue;
                    }

                    int row = group.Key;
                    int col = cells[0].Column;
                    list.Add(new ContentStreamBlock
                    {
                        Kind = ContentBlockKind.LooseRow,
                        Row = row,
                        Column = col,
                        OrderHint = OrderKey(row, col),
                        Id = "C:R" + row.ToString(CultureInfo.InvariantCulture),
                        Signature = "C|" + LooseRowSignature(cells),
                        Cells = cells
                    });
                }
            }

            list.Sort(CompareBlocks);
            return list;
        }

        /// <summary>
        /// 左右ストリームを SequenceAligner で対応付ける。
        /// </summary>
        public static IList<ContentStreamPair> Align(
            IList<ContentStreamBlock> left,
            IList<ContentStreamBlock> right)
        {
            IList<ContentStreamBlock> l = left ?? Array.Empty<ContentStreamBlock>();
            IList<ContentStreamBlock> r = right ?? Array.Empty<ContentStreamBlock>();
            IList<AlignStep> steps = SequenceAligner.Align(
                l.Count,
                r.Count,
                (i, j) => BlockSimilarity(l[i], r[j]),
                MatchThreshold,
                SkipCost);

            var pairs = new List<ContentStreamPair>();
            foreach (AlignStep step in steps)
            {
                if (step == null)
                {
                    continue;
                }

                ContentStreamBlock lb = null;
                ContentStreamBlock rb = null;
                if ((step.Op == AlignOp.Match || step.Op == AlignOp.SkipLeft)
                    && step.LeftIndex >= 0 && step.LeftIndex < l.Count)
                {
                    lb = l[step.LeftIndex];
                }

                if ((step.Op == AlignOp.Match || step.Op == AlignOp.SkipRight)
                    && step.RightIndex >= 0 && step.RightIndex < r.Count)
                {
                    rb = r[step.RightIndex];
                }

                if (lb == null && rb == null)
                {
                    continue;
                }

                pairs.Add(new ContentStreamPair
                {
                    Op = step.Op,
                    Left = lb,
                    Right = rb
                });
            }

            return pairs;
        }

        /// <summary>
        /// OrderHint に最も近いペア index を返す（見つからなければ -1）。
        /// </summary>
        public static int FindNearestPairIndex(IList<ContentStreamPair> pairs, double orderHint)
        {
            if (pairs == null || pairs.Count == 0)
            {
                return -1;
            }

            if (orderHint <= 0)
            {
                return 0;
            }

            int best = 0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < pairs.Count; i++)
            {
                ContentStreamPair p = pairs[i];
                if (p == null)
                {
                    continue;
                }

                double h = p.OrderHint;
                if (h <= 0)
                {
                    continue;
                }

                double d = Math.Abs(h - orderHint);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// ブロック類似度 0..1。種別不一致は 0。
        /// </summary>
        public static double BlockSimilarity(ContentStreamBlock a, ContentStreamBlock b)
        {
            if (a == null || b == null || a.Kind != b.Kind)
            {
                return 0;
            }

            if (string.Equals(a.Signature, b.Signature, StringComparison.Ordinal))
            {
                return 1.0;
            }

            switch (a.Kind)
            {
                case ContentBlockKind.LooseRow:
                    return TextOverlap(a.Signature, b.Signature);
                case ContentBlockKind.Table:
                    return TableBlockSimilarity(a.Table, b.Table);
                case ContentBlockKind.Image:
                    return ImageSimilarity(a.Image, b.Image);
                case ContentBlockKind.Shape:
                    return ShapeSimilarity(a.Shape, b.Shape);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 並び比較（行→列→種別）。
        /// </summary>
        private static int CompareBlocks(ContentStreamBlock x, ContentStreamBlock y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            int c = x.Row.CompareTo(y.Row);
            if (c != 0)
            {
                return c;
            }

            c = x.Column.CompareTo(y.Column);
            if (c != 0)
            {
                return c;
            }

            return ((int)x.Kind).CompareTo((int)y.Kind);
        }

        /// <summary>
        /// OrderHint = row * 1000 + col（簡易）。
        /// </summary>
        private static double OrderKey(int row, int col)
        {
            int r = Math.Max(0, row);
            int c = Math.Max(0, col);
            if (c > 999)
            {
                c = 999;
            }

            return r * 1000.0 + c;
        }

        private static string LooseRowSignature(IList<CellContent> cells)
        {
            var sb = new StringBuilder();
            foreach (CellContent cell in cells)
            {
                if (cell == null)
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append('\t');
                }

                sb.Append(cell.Text ?? string.Empty);
                sb.Append('\0');
                sb.Append(cell.BackgroundArgb ?? string.Empty);
            }

            return sb.ToString();
        }

        private static string TableSignature(TableBlock table)
        {
            if (table == null || table.Rows == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (IList<CellContent> row in table.Rows)
            {
                if (row == null)
                {
                    continue;
                }

                foreach (CellContent cell in row)
                {
                    sb.Append(cell != null ? (cell.Text ?? string.Empty) : string.Empty);
                    sb.Append('|');
                }

                sb.Append(';');
            }

            return sb.ToString();
        }

        private static double TableBlockSimilarity(TableBlock a, TableBlock b)
        {
            if (a == null || b == null || a.Rows == null || b.Rows == null)
            {
                return 0;
            }

            var setA = new HashSet<string>(StringComparer.Ordinal);
            var setB = new HashSet<string>(StringComparer.Ordinal);
            CollectRowKeys(a, setA);
            CollectRowKeys(b, setB);
            if (setA.Count == 0 && setB.Count == 0)
            {
                return 1.0;
            }

            int inter = setA.Count(setB.Contains);
            int union = setA.Count + setB.Count - inter;
            return union <= 0 ? 0 : (double)inter / union;
        }

        private static void CollectRowKeys(TableBlock table, HashSet<string> set)
        {
            foreach (IList<CellContent> row in table.Rows)
            {
                if (row == null)
                {
                    continue;
                }

                var sb = new StringBuilder();
                foreach (CellContent cell in row)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append('\t');
                    }

                    sb.Append(cell != null ? (cell.Text ?? string.Empty) : string.Empty);
                }

                set.Add(sb.ToString());
            }
        }

        private static double ImageSimilarity(EmbeddedImage a, EmbeddedImage b)
        {
            if (a == null || b == null)
            {
                return 0;
            }

            if (!string.IsNullOrEmpty(a.ContentHash)
                && string.Equals(a.ContentHash, b.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            if (!string.IsNullOrEmpty(a.FileName)
                && string.Equals(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return 0.7;
            }

            // 見た目類似は重いのでストリーム構築ではハッシュ優先。異なるハッシュは中程度の近さ。
            return 0.2;
        }

        private static double ShapeSimilarity(ShapeContent a, ShapeContent b)
        {
            if (a == null || b == null)
            {
                return 0;
            }

            if (!string.IsNullOrEmpty(a.ContentHash)
                && string.Equals(a.ContentHash, b.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            if (!string.IsNullOrEmpty(a.Text)
                && string.Equals(a.Text, b.Text, StringComparison.Ordinal))
            {
                return 0.85;
            }

            if (!string.IsNullOrEmpty(a.Kind)
                && string.Equals(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase))
            {
                return 0.4;
            }

            return 0;
        }

        private static double TextOverlap(string sa, string sb)
        {
            if (string.IsNullOrEmpty(sa) && string.IsNullOrEmpty(sb))
            {
                return 1.0;
            }

            if (string.IsNullOrEmpty(sa) || string.IsNullOrEmpty(sb))
            {
                return 0;
            }

            // 完全一致以外は簡易: 共通プレフィックス比
            int n = Math.Min(sa.Length, sb.Length);
            int common = 0;
            for (int i = 0; i < n; i++)
            {
                if (sa[i] != sb[i])
                {
                    break;
                }

                common++;
            }

            int max = Math.Max(sa.Length, sb.Length);
            return max <= 0 ? 0 : (double)common / max;
        }
    }
}
