using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DiffXL.COMMON;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 統一内容ストリーム上のブロック種別（シート上の出現順で並べる）。
    /// </summary>
    public enum ContentBlockKind
    {
        /// <summary>テーブル外セルの 1 行分。</summary>
        LooseRow,

        /// <summary>ボーダー検出テーブル（未展開時・互換）。</summary>
        Table,

        /// <summary>展開後のテーブル見出し帯。</summary>
        TableHeader,

        /// <summary>展開後のテーブル 1 表示行。</summary>
        TableRow,

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
    /// 左右 ContentPane が共有するストリーム表示レイアウト（展開済みペア＋行高マップ）。
    /// Visual は持たず、オフセット計算と仮想化に使う。
    /// </summary>
    public sealed class ContentStreamLayout
    {
        private readonly double[] _heights;
        private readonly double[] _offsets;
        private double _total;

        /// <summary>
        /// 展開済みアライン列からレイアウトを構築する。
        /// </summary>
        public ContentStreamLayout(IList<ContentStreamPair> pairs)
        {
            Pairs = pairs ?? Array.Empty<ContentStreamPair>();
            int n = Pairs.Count;
            _heights = new double[n];
            _offsets = new double[n + 1];
            for (int i = 0; i < n; i++)
            {
                _heights[i] = EstimatePairHeight(Pairs[i]);
            }

            RebuildOffsets();
        }

        /// <summary>アライン済みペア（表は行展開済み）。</summary>
        public IList<ContentStreamPair> Pairs { get; private set; }

        /// <summary>総高さ（スペーサ合計）。</summary>
        public double TotalHeight
        {
            get { return _total; }
        }

        /// <summary>ペア数。</summary>
        public int Count
        {
            get { return Pairs != null ? Pairs.Count : 0; }
        }

        /// <summary>index の推定／補正後高さ。</summary>
        public double GetHeight(int index)
        {
            if (index < 0 || index >= _heights.Length)
            {
                return 0;
            }

            return _heights[index];
        }

        /// <summary>index 先頭の Y オフセット。</summary>
        public double OffsetOf(int index)
        {
            if (index <= 0)
            {
                return 0;
            }

            if (index >= _offsets.Length)
            {
                return _total;
            }

            return _offsets[index];
        }

        /// <summary>
        /// Y 位置を含むペア index（範囲外は端にクランプ）。
        /// </summary>
        public int IndexAtOffset(double y)
        {
            int n = _heights.Length;
            if (n == 0)
            {
                return 0;
            }

            if (y <= 0)
            {
                return 0;
            }

            if (y >= _total)
            {
                return n - 1;
            }

            // 線形探索で十分（数千行）。必要なら後で二分探索。
            int lo = 0;
            int hi = n;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) / 2);
                if (_offsets[mid + 1] <= y)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return Math.Min(lo, n - 1);
        }

        /// <summary>
        /// 高さマップが変わった（左右ペインの行高強制同期用）。
        /// </summary>
        public event Action HeightsChanged;

        /// <summary>
        /// 実測高さでマップを更新する。左右共有のため縮めず伸ばすのみ（有意に伸びたとき true）。
        /// </summary>
        public bool TryUpdateHeight(int index, double height)
        {
            if (index < 0 || index >= _heights.Length)
            {
                return false;
            }

            if (height < 1)
            {
                return false;
            }

            double old = _heights[index];
            // 片側の短い実測で共通マップを縮めると相手側の同期が崩れる
            if (height <= old + 1.0)
            {
                return false;
            }

            _heights[index] = height;
            RebuildOffsets();
            Action handler = HeightsChanged;
            if (handler != null)
            {
                handler();
            }

            return true;
        }

        /// <summary>
        /// 指定 index がテーブル行／見出しペアなら true（左右で高さを強制一致させる対象）。
        /// </summary>
        public bool IsUniformHeightPair(int index)
        {
            if (Pairs == null || index < 0 || index >= Pairs.Count)
            {
                return false;
            }

            ContentStreamPair p = Pairs[index];
            if (p == null)
            {
                return false;
            }

            ContentStreamBlock b = p.Left ?? p.Right;
            if (b == null)
            {
                return false;
            }

            return b.Kind == ContentBlockKind.TableRow
                || b.Kind == ContentBlockKind.TableHeader;
        }

        private void RebuildOffsets()
        {
            double sum = 0;
            _offsets[0] = 0;
            for (int i = 0; i < _heights.Length; i++)
            {
                sum += _heights[i];
                _offsets[i + 1] = sum;
            }

            _total = sum;
        }

        /// <summary>
        /// ペアの初期推定高さ（左右 max）。ギャップは相手相当。
        /// </summary>
        public static double EstimatePairHeight(ContentStreamPair pair)
        {
            if (pair == null)
            {
                return 48;
            }

            double leftH = EstimateBlockHeight(pair.Left);
            double rightH = EstimateBlockHeight(pair.Right);
            double h = Math.Max(leftH, rightH);
            return h > 1 ? h : 48;
        }

        /// <summary>
        /// ブロック 1 個の表示高推定（外側 margin 込み）。
        /// </summary>
        public static double EstimateBlockHeight(ContentStreamBlock block)
        {
            if (block == null)
            {
                return 0;
            }

            // 外側 Border margin 10 + padding 相当
            const double outer = 12;
            switch (block.Kind)
            {
                case ContentBlockKind.TableHeader:
                    // 見出し 1 ブロック分（左右で同じ固定高）
                    return 44;
                case ContentBlockKind.TableRow:
                    // テーブル 1 行分（ギャップも同じ高に揃える）
                    return 36;
                case ContentBlockKind.Table:
                    {
                        int rows = block.Table != null && block.Table.Rows != null
                            ? block.Table.Rows.Count
                            : 1;
                        return outer + 48 + Math.Max(1, rows) * 34;
                    }
                case ContentBlockKind.LooseRow:
                    {
                        int cells = block.Cells != null ? block.Cells.Count : 1;
                        return outer + 22 + Math.Max(1, cells) * 34;
                    }
                case ContentBlockKind.Image:
                    return outer + EstimateImageHeight(block.Image);
                case ContentBlockKind.Shape:
                    return outer + 48;
                default:
                    return outer + 48;
            }
        }

        private static double EstimateImageHeight(EmbeddedImage image)
        {
            const double maxW = 320.0;
            const double maxH = 240.0;
            int pw = image != null && image.PixelWidth > 0 ? image.PixelWidth : 200;
            int ph = image != null && image.PixelHeight > 0 ? image.PixelHeight : 140;
            if (pw < 1)
            {
                pw = 1;
            }

            if (ph < 1)
            {
                ph = 1;
            }

            double scale = Math.Min(1.0, Math.Min(maxW / pw, maxH / ph));
            double dispH = Math.Max(1.0, Math.Round(ph * scale));
            return 16 + 18 + 4 + 16 + 8 + dispH + 12 + 8 + 22;
        }
    }

    /// <summary>
    /// シート内容をドキュメント順の統一ストリームに組み立て、左右をアラインする。
    /// </summary>
    public static class ContentStreamBuilder
    {
        private static readonly object LayoutCacheLock = new object();
        private static SheetContent _layoutCacheLeft;
        private static SheetContent _layoutCacheRight;
        private static ContentStreamLayout _layoutCache;

        /// <summary>
        /// ブロック一致の類似度しきい値（SequenceAligner 共通）。
        /// </summary>
        public const double MatchThreshold = 0.55;

        /// <summary>
        /// テーブル同士をストリーム上で対応させる最低 Jaccard。
        /// <see cref="TableCompareService"/> の TableMatchThreshold(0.3) と揃える。
        /// これ未満は Match 不可、以上は SequenceAligner 用に MatchThreshold 以上へスケールする。
        /// </summary>
        public const double TablePairMinSimilarity = 0.3;

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
        /// 左右シートから展開済みストリームレイアウトを構築する。
        /// 同一シート参照の連続呼び出しはキャッシュを返す（左右ペイン二重計算防止）。
        /// </summary>
        public static ContentStreamLayout GetOrBuildLayout(SheetContent leftSheet, SheetContent rightSheet)
        {
            lock (LayoutCacheLock)
            {
                if (_layoutCache != null
                    && ReferenceEquals(_layoutCacheLeft, leftSheet)
                    && ReferenceEquals(_layoutCacheRight, rightSheet))
                {
                    return _layoutCache;
                }

                ContentStreamLayout layout = BuildLayout(leftSheet, rightSheet);
                _layoutCacheLeft = leftSheet;
                _layoutCacheRight = rightSheet;
                _layoutCache = layout;
                return layout;
            }
        }

        /// <summary>
        /// レイアウトキャッシュを破棄する（新比較・明示リロード時）。
        /// </summary>
        public static void ClearLayoutCache()
        {
            lock (LayoutCacheLock)
            {
                _layoutCacheLeft = null;
                _layoutCacheRight = null;
                _layoutCache = null;
            }
        }

        /// <summary>
        /// ブロック Align 後にテーブルをヘッダ＋行へ展開したレイアウトを返す。
        /// </summary>
        public static ContentStreamLayout BuildLayout(SheetContent leftSheet, SheetContent rightSheet)
        {
            IList<ContentStreamBlock> leftBlocks = Build(leftSheet);
            IList<ContentStreamBlock> rightBlocks = Build(rightSheet);
            IList<ContentStreamPair> blockPairs = Align(leftBlocks, rightBlocks);
            IList<ContentStreamPair> expanded = ExpandTables(blockPairs);
            return new ContentStreamLayout(expanded);
        }

        /// <summary>
        /// テーブルブロックのペアを TableHeader + TableRow 列に展開する。
        /// 非テーブルはそのまま。
        /// </summary>
        public static IList<ContentStreamPair> ExpandTables(IList<ContentStreamPair> pairs)
        {
            var result = new List<ContentStreamPair>();
            if (pairs == null || pairs.Count == 0)
            {
                return result;
            }

            foreach (ContentStreamPair pair in pairs)
            {
                if (pair == null)
                {
                    continue;
                }

                ContentStreamBlock left = pair.Left;
                ContentStreamBlock right = pair.Right;
                bool leftTable = left != null && left.Kind == ContentBlockKind.Table;
                bool rightTable = right != null && right.Kind == ContentBlockKind.Table;

                if (!leftTable && !rightTable)
                {
                    result.Add(pair);
                    continue;
                }

                // 片側がテーブルでない場合は展開せずそのまま（種別不一致の保険）
                if ((left != null && !leftTable) || (right != null && !rightTable))
                {
                    result.Add(pair);
                    continue;
                }

                ExpandOneTablePair(pair, result);
            }

            return result;
        }

        /// <summary>
        /// 1 テーブル対応をヘッダ＋行ペアへ展開する。
        /// </summary>
        private static void ExpandOneTablePair(ContentStreamPair pair, List<ContentStreamPair> result)
        {
            TableBlock leftTable = pair.Left != null ? pair.Left.Table : null;
            TableBlock rightTable = pair.Right != null ? pair.Right.Table : null;

            IList<IList<CellContent>> leftRows =
                leftTable != null && leftTable.Rows != null
                    ? leftTable.Rows
                    : Array.Empty<IList<CellContent>>();
            IList<IList<CellContent>> rightRows =
                rightTable != null && rightTable.Rows != null
                    ? rightTable.Rows
                    : Array.Empty<IList<CellContent>>();

            // ヘッダ
            result.Add(new ContentStreamPair
            {
                Op = pair.Op,
                Left = leftTable != null ? MakeTableHeaderBlock(leftTable, pair.Left) : null,
                Right = rightTable != null ? MakeTableHeaderBlock(rightTable, pair.Right) : null
            });

            IList<AlignStep> steps;
            if (leftTable != null && rightTable != null)
            {
                steps = TableRowAligner.AlignRows(leftRows, rightRows);
            }
            else if (leftTable != null)
            {
                steps = new List<AlignStep>(leftRows.Count);
                for (int i = 0; i < leftRows.Count; i++)
                {
                    steps.Add(new AlignStep
                    {
                        Op = AlignOp.SkipLeft,
                        LeftIndex = i,
                        RightIndex = -1
                    });
                }
            }
            else
            {
                steps = new List<AlignStep>(rightRows.Count);
                for (int j = 0; j < rightRows.Count; j++)
                {
                    steps.Add(new AlignStep
                    {
                        Op = AlignOp.SkipRight,
                        LeftIndex = -1,
                        RightIndex = j
                    });
                }
            }

            foreach (AlignStep step in steps)
            {
                if (step == null)
                {
                    continue;
                }

                ContentStreamBlock lb = null;
                ContentStreamBlock rb = null;
                if ((step.Op == AlignOp.Match || step.Op == AlignOp.SkipLeft)
                    && step.LeftIndex >= 0 && step.LeftIndex < leftRows.Count)
                {
                    lb = MakeTableRowBlock(leftTable, leftRows[step.LeftIndex], step.LeftIndex, pair.Left);
                }

                if ((step.Op == AlignOp.Match || step.Op == AlignOp.SkipRight)
                    && step.RightIndex >= 0 && step.RightIndex < rightRows.Count)
                {
                    rb = MakeTableRowBlock(rightTable, rightRows[step.RightIndex], step.RightIndex, pair.Right);
                }

                if (lb == null && rb == null)
                {
                    continue;
                }

                result.Add(new ContentStreamPair
                {
                    Op = step.Op,
                    Left = lb,
                    Right = rb
                });
            }
        }

        private static ContentStreamBlock MakeTableHeaderBlock(TableBlock table, ContentStreamBlock source)
        {
            int row = table != null ? table.RowStart : (source != null ? source.Row : 0);
            int col = table != null ? table.ColStart : (source != null ? source.Column : 0);
            return new ContentStreamBlock
            {
                Kind = ContentBlockKind.TableHeader,
                Row = row,
                Column = col,
                OrderHint = source != null && source.OrderHint > 0
                    ? source.OrderHint
                    : (row * 1000.0 + Math.Min(999, Math.Max(0, col))),
                Id = "TH:" + (table != null ? table.Id ?? string.Empty : string.Empty),
                Signature = "TH|" + (table != null ? table.Id ?? string.Empty : string.Empty),
                Table = table
            };
        }

        private static ContentStreamBlock MakeTableRowBlock(
            TableBlock table,
            IList<CellContent> rowCells,
            int rowIndex,
            ContentStreamBlock source)
        {
            int excelRow = ResolveExcelRow(rowCells, table, rowIndex);
            int col = table != null ? table.ColStart : (source != null ? source.Column : 0);
            double baseHint = source != null && source.OrderHint > 0
                ? source.OrderHint
                : (table != null ? table.RowStart * 1000.0 + Math.Min(999, Math.Max(0, col)) : 0);
            return new ContentStreamBlock
            {
                Kind = ContentBlockKind.TableRow,
                Row = excelRow,
                Column = col,
                // 同一表内で行順を保つ（OrderHint は表基準 + 行 index）
                OrderHint = baseHint + (rowIndex + 1) * 0.001,
                Id = "TR:" + (table != null ? table.Id ?? string.Empty : string.Empty)
                    + ":" + rowIndex.ToString(CultureInfo.InvariantCulture),
                Signature = "TR|" + TableRowAligner.MakeRowKey(rowCells),
                Table = table,
                Cells = rowCells
            };
        }

        private static int ResolveExcelRow(IList<CellContent> rowCells, TableBlock table, int rowIndex)
        {
            if (rowCells != null)
            {
                for (int i = 0; i < rowCells.Count; i++)
                {
                    CellContent c = rowCells[i];
                    if (c != null && c.Row > 0)
                    {
                        return c.Row;
                    }
                }
            }

            if (table != null && table.RowStart > 0)
            {
                return table.RowStart + rowIndex;
            }

            return rowIndex + 1;
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
                    return LooseRowSimilarity(a, b);
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

        /// <summary>
        /// テーブル類似度。行キー多重集合 Jaccard と行 LCS ソフト類似度の大きい方を使い、
        /// ペア可能なら SequenceAligner の MatchThreshold 以上へスケールする。
        /// （製品カタログのように行 ID は同じで状態列だけ違う表も対応できる）
        /// </summary>
        private static double TableBlockSimilarity(TableBlock a, TableBlock b)
        {
            if (a == null || b == null || a.Rows == null || b.Rows == null)
            {
                return 0;
            }

            List<string> keysA = CollectRowKeyList(a);
            List<string> keysB = CollectRowKeyList(b);
            double jaccard = MultisetJaccard(keysA, keysB);
            // 行キー Jaccard が既に十分なら高コストな行 LCS は省略（長大表のストリーム Align 用）
            double raw = jaccard;
            if (jaccard < TablePairMinSimilarity)
            {
                double soft = TableRowAligner.SoftTableSimilarity(a, b);
                raw = Math.Max(jaccard, soft);
            }

            if (raw < TablePairMinSimilarity)
            {
                return raw;
            }

            // [TablePairMinSimilarity, 1] → [MatchThreshold, 1]
            double span = 1.0 - TablePairMinSimilarity;
            if (span <= 0)
            {
                return 1.0;
            }

            double t = (raw - TablePairMinSimilarity) / span;
            return MatchThreshold + (1.0 - MatchThreshold) * t;
        }

        private static List<string> CollectRowKeyList(TableBlock table)
        {
            var keys = new List<string>();
            if (table == null || table.Rows == null)
            {
                return keys;
            }

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

                keys.Add(sb.ToString());
            }

            return keys;
        }

        /// <summary>
        /// セル行同士をストリーム上で対応させる最低類似度（これ未満は Match 不可）。
        /// 行番号が違っても「検証メモ」同士など近い文言は bigram / トークンで拾う。
        /// </summary>
        public const double LooseRowPairMinSimilarity = 0.28;

        /// <summary>
        /// テーブル外セル行の類似度。完全一致・セル多重集合 Jaccard・ソフトセル対応・
        /// 文字 bigram / トークン重複の最大値。
        /// 同一行番号なら Match 可能に底上げ。行番号が違っても PairMin 以上なら UI 閾値へスケール。
        /// </summary>
        private static double LooseRowSimilarity(ContentStreamBlock a, ContentStreamBlock b)
        {
            if (a == null || b == null)
            {
                return 0;
            }

            if (string.Equals(a.Signature, b.Signature, StringComparison.Ordinal))
            {
                return 1.0;
            }

            double prefix = TextOverlap(a.Signature, b.Signature);
            double bag = MultisetJaccard(CellTextKeys(a.Cells), CellTextKeys(b.Cells));
            double soft = SoftCellOverlap(a.Cells, b.Cells);
            string ta = JoinCellTexts(a.Cells);
            string tb = JoinCellTexts(b.Cells);
            double bigram = CharBigramDice(ta, tb);
            double tokens = TokenOverlap(ta, tb);
            double sim = Math.Max(prefix, Math.Max(bag, Math.Max(soft, Math.Max(bigram, tokens))));

            // 同一 Excel 行のセル行はストリーム上で横並び対応（内容差はセル強調で示す）
            if (a.Row > 0 && a.Row == b.Row)
            {
                sim = Math.Min(1.0, Math.Max(sim, MatchThreshold));
                return sim;
            }

            // 行番号が違っても内容が近ければ対応（B17↔B19 の検証メモ等）
            if (sim >= LooseRowPairMinSimilarity)
            {
                double span = 1.0 - LooseRowPairMinSimilarity;
                if (span <= 1e-9)
                {
                    return 1.0;
                }

                double t = (sim - LooseRowPairMinSimilarity) / span;
                return MatchThreshold + (1.0 - MatchThreshold) * t;
            }

            return sim;
        }

        private static string JoinCellTexts(IList<CellContent> cells)
        {
            if (cells == null || cells.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (CellContent cell in cells)
            {
                if (cell == null || string.IsNullOrEmpty(cell.Text))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(cell.Text);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 文字 bigram の Dice 係数（日本語の部分一致に強い）。
        /// </summary>
        private static double CharBigramDice(string a, string b)
        {
            a = a ?? string.Empty;
            b = b ?? string.Empty;
            if (a.Length == 0 && b.Length == 0)
            {
                return 1.0;
            }

            if (a.Length < 2 || b.Length < 2)
            {
                return TextOverlap(a, b);
            }

            var ba = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < a.Length - 1; i++)
            {
                string g = a.Substring(i, 2);
                int c;
                ba.TryGetValue(g, out c);
                ba[g] = c + 1;
            }

            var bb = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < b.Length - 1; i++)
            {
                string g = b.Substring(i, 2);
                int c;
                bb.TryGetValue(g, out c);
                bb[g] = c + 1;
            }

            int inter = 0;
            int sumA = 0;
            int sumB = 0;
            foreach (KeyValuePair<string, int> kv in ba)
            {
                sumA += kv.Value;
                int rc;
                bb.TryGetValue(kv.Key, out rc);
                inter += Math.Min(kv.Value, rc);
            }

            foreach (int v in bb.Values)
            {
                sumB += v;
            }

            int denom = sumA + sumB;
            return denom <= 0 ? 0 : (2.0 * inter) / denom;
        }

        /// <summary>
        /// 空白・記号で区切ったトークンと、連続する日本語/英数のかたまりの Jaccard。
        /// </summary>
        private static double TokenOverlap(string a, string b)
        {
            HashSet<string> ta = ExtractTokens(a);
            HashSet<string> tb = ExtractTokens(b);
            if (ta.Count == 0 && tb.Count == 0)
            {
                return 1.0;
            }

            if (ta.Count == 0 || tb.Count == 0)
            {
                return 0;
            }

            int inter = ta.Count(tb.Contains);
            int union = ta.Count + tb.Count - inter;
            return union <= 0 ? 0 : (double)inter / union;
        }

        private static HashSet<string> ExtractTokens(string s)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(s))
            {
                return set;
            }

            var cur = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (char.IsLetterOrDigit(ch) || (ch >= 0x3040 && ch <= 0x30FF) || (ch >= 0x4E00 && ch <= 0x9FFF)
                    || (ch >= 0xFF10 && ch <= 0xFF19) || (ch >= 0xFF21 && ch <= 0xFF3A) || (ch >= 0xFF41 && ch <= 0xFF5A))
                {
                    cur.Append(ch);
                }
                else
                {
                    if (cur.Length >= 2)
                    {
                        set.Add(cur.ToString());
                    }

                    cur.Length = 0;
                }
            }

            if (cur.Length >= 2)
            {
                set.Add(cur.ToString());
            }

            return set;
        }

        private static List<string> CellTextKeys(IList<CellContent> cells)
        {
            var keys = new List<string>();
            if (cells == null)
            {
                return keys;
            }

            foreach (CellContent cell in cells)
            {
                if (cell == null)
                {
                    continue;
                }

                keys.Add(cell.Text ?? string.Empty);
            }

            return keys;
        }

        /// <summary>
        /// セル同士をソフト対応（完全一致 / 一方が他方の接頭辞）し、対応数 / max(|L|,|R|) を返す。
        /// </summary>
        private static double SoftCellOverlap(IList<CellContent> left, IList<CellContent> right)
        {
            List<string> la = CellTextKeys(left);
            List<string> rb = CellTextKeys(right);
            if (la.Count == 0 && rb.Count == 0)
            {
                return 1.0;
            }

            if (la.Count == 0 || rb.Count == 0)
            {
                return 0;
            }

            var usedRight = new bool[rb.Count];
            double matched = 0;
            for (int i = 0; i < la.Count; i++)
            {
                int bestJ = -1;
                double best = 0;
                for (int j = 0; j < rb.Count; j++)
                {
                    if (usedRight[j])
                    {
                        continue;
                    }

                    double s = SoftTextSimilarity(la[i], rb[j]);
                    if (s > best)
                    {
                        best = s;
                        bestJ = j;
                    }
                }

                if (bestJ >= 0 && best >= 0.5)
                {
                    usedRight[bestJ] = true;
                    matched += best;
                }
            }

            int denom = Math.Max(la.Count, rb.Count);
            return denom <= 0 ? 0 : matched / denom;
        }

        private static double SoftTextSimilarity(string a, string b)
        {
            a = a ?? string.Empty;
            b = b ?? string.Empty;
            if (string.Equals(a, b, StringComparison.Ordinal))
            {
                return 1.0;
            }

            if (a.Length == 0 || b.Length == 0)
            {
                return 0;
            }

            // 一方が他方の接頭辞（「共通アンカー」vs「共通アンカー（右メモあり）」）
            string shorter = a.Length <= b.Length ? a : b;
            string longer = a.Length <= b.Length ? b : a;
            double prefixScore = 0;
            if (longer.StartsWith(shorter, StringComparison.Ordinal))
            {
                prefixScore = (double)shorter.Length / longer.Length;
            }

            double overlap = TextOverlap(a, b);
            double bigram = CharBigramDice(a, b);
            double tokens = TokenOverlap(a, b);
            return Math.Max(prefixScore, Math.Max(overlap, Math.Max(bigram, tokens)));
        }

        /// <summary>
        /// 多重集合 Jaccard（min 出現 / max 出現 の和）。
        /// </summary>
        private static double MultisetJaccard(IList<string> left, IList<string> right)
        {
            if (left == null)
            {
                left = Array.Empty<string>();
            }

            if (right == null)
            {
                right = Array.Empty<string>();
            }

            if (left.Count == 0 && right.Count == 0)
            {
                return 1.0;
            }

            if (left.Count == 0 || right.Count == 0)
            {
                return 0;
            }

            var leftCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string k in left)
            {
                int c;
                leftCount.TryGetValue(k, out c);
                leftCount[k] = c + 1;
            }

            var rightCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string k in right)
            {
                int c;
                rightCount.TryGetValue(k, out c);
                rightCount[k] = c + 1;
            }

            int inter = 0;
            int union = 0;
            var all = new HashSet<string>(leftCount.Keys, StringComparer.Ordinal);
            foreach (string k in rightCount.Keys)
            {
                all.Add(k);
            }

            foreach (string k in all)
            {
                int lc;
                int rc;
                leftCount.TryGetValue(k, out lc);
                rightCount.TryGetValue(k, out rc);
                inter += Math.Min(lc, rc);
                union += Math.Max(lc, rc);
            }

            return union <= 0 ? 1.0 : (double)inter / union;
        }

        /// <summary>
        /// 画像類似度。<see cref="ImageSequenceAligner"/> と同じ見た目比較を使い、
        /// 似ている画像（部分差分含む）をストリーム上でも Match できるようにする。
        /// 系列アライン側の Match 下限（1 - RejectDiffRatio）以上なら UI 閾値以上へスケールする。
        /// </summary>
        private static double ImageSimilarity(EmbeddedImage a, EmbeddedImage b)
        {
            if (a == null || b == null)
            {
                return 0;
            }

            double sim = ImageSequenceAligner.ComputeSimilarity(a, b);
            if (sim <= 0)
            {
                return 0;
            }

            // ImageSequenceAligner の Match 下限（既定 Reject=0.85 → 類似度 0.15）
            double alignMin = 0.15;
            try
            {
                if (AppSettings.Current != null && AppSettings.Current.Diff != null)
                {
                    alignMin = 1.0 - AppSettings.Current.Diff.ImageRejectDiffRatio;
                }
                else
                {
                    alignMin = 1.0 - ImageDiffService.RejectDiffRatio;
                }
            }
            catch
            {
                alignMin = 1.0 - ImageDiffService.RejectDiffRatio;
            }

            if (alignMin < 0)
            {
                alignMin = 0;
            }

            if (sim < alignMin)
            {
                return sim;
            }

            // [alignMin, 1] → [MatchThreshold, 1]（ストリームの MatchThreshold 0.55 を満たす）
            double span = 1.0 - alignMin;
            if (span <= 1e-9)
            {
                return 1.0;
            }

            double t = (sim - alignMin) / span;
            return MatchThreshold + (1.0 - MatchThreshold) * t;
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
