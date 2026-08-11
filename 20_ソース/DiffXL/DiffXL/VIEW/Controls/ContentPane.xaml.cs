using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// 1 シート分の内容ベース比較表示ホスト（セル／テーブル／画像／図形タブ）。
    /// Excel 埋め込みは使わない。
    /// </summary>
    public partial class ContentPane : UserControl
    {
        /// <summary>
        /// テーブル間マッチの粗類似度しきい値（TableCompareService と揃える）。
        /// </summary>
        private const double TableMatchThreshold = 0.3;

        /// <summary>
        /// テーブル Skip コスト。
        /// </summary>
        private const double TableSkipCost = 0.4;

        /// <summary>
        /// 現在表示中のシート。
        /// </summary>
        private SheetContent _sheet;

        /// <summary>
        /// 相手側シート（行アライン用。null 可）。
        /// </summary>
        private SheetContent _partnerSheet;

        /// <summary>
        /// 現在シートに紐づく差分。
        /// </summary>
        private IList<DiffItem> _sheetDiffs = new List<DiffItem>();

        /// <summary>
        /// 左ペインかどうか。
        /// </summary>
        private bool _isLeft = true;

        /// <summary>
        /// 画像ハイライト（枠・塗り）の表示フラグ。トグルで切替、再比較不要。
        /// </summary>
        private bool _highlightVisible = true;

        /// <summary>
        /// 現在表示中の ImagePairView 一覧（トグル伝播用）。
        /// </summary>
        private readonly List<ImagePairView> _imagePairViews = new List<ImagePairView>();

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public ContentPane()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 画像ハイライトの表示／非表示。画像本体は残し枠・塗りだけ切替（再比較不要）。
        /// </summary>
        public bool HighlightVisible
        {
            get { return _highlightVisible; }
        }

        /// <summary>
        /// 画像ハイライト表示を全 ImagePairView に伝播する。
        /// </summary>
        /// <param name="visible">表示するなら true</param>
        public void SetHighlightVisible(bool visible)
        {
            _highlightVisible = visible;
            foreach (ImagePairView view in _imagePairViews)
            {
                if (view != null)
                {
                    view.SetHighlightVisible(visible);
                }
            }
        }

        /// <summary>
        /// 設定の画像ハイライト色を全ペアに再適用する。
        /// </summary>
        public void RefreshImageHighlightStyle()
        {
            foreach (ImagePairView view in _imagePairViews)
            {
                if (view != null)
                {
                    view.RefreshStyleFromSettings();
                }
            }
        }

        /// <summary>
        /// 表示中シート名。
        /// </summary>
        public string SheetName
        {
            get { return _sheet != null ? _sheet.Name : null; }
        }

        /// <summary>
        /// シート内容と差分を読み込み、各タブを更新する。
        /// </summary>
        /// <param name="sheet">シート内容（null 可）</param>
        /// <param name="sheetDiffs">このシート関連の差分</param>
        /// <param name="isLeft">左ペインなら true</param>
        public void Load(SheetContent sheet, IList<DiffItem> sheetDiffs, bool isLeft)
        {
            Load(sheet, sheetDiffs, isLeft, partnerSheet: null);
        }

        /// <summary>
        /// シート内容・相手シート・差分を読み込み、各タブを更新する。
        /// </summary>
        /// <param name="sheet">シート内容（null 可）</param>
        /// <param name="sheetDiffs">このシート関連の差分</param>
        /// <param name="isLeft">左ペインなら true</param>
        /// <param name="partnerSheet">相手側シート（テーブル行アライン用。null 可）</param>
        public void Load(
            SheetContent sheet,
            IList<DiffItem> sheetDiffs,
            bool isLeft,
            SheetContent partnerSheet)
        {
            _sheet = sheet;
            _partnerSheet = partnerSheet;
            _sheetDiffs = sheetDiffs ?? new List<DiffItem>();
            _isLeft = isLeft;

            string side = isLeft ? "左" : "右";
            if (sheet == null)
            {
                HeaderText.Text = side + " · シートなし";
                CellsSummary.Text = "セル（テーブル外）: —";
                TablesSummary.Text = "テーブル: —";
                ImagesSummary.Text = "画像: —";
                ShapesSummary.Text = "図形: —";
                CellsList.ItemsSource = null;
                ClearTablesHost();
                ClearImagesHost();
                ShapesList.ItemsSource = null;
                return;
            }

            int cellCount = sheet.LooseCells != null ? sheet.LooseCells.Count : 0;
            int tableCount = sheet.Tables != null ? sheet.Tables.Count : 0;
            int imageCount = sheet.Images != null ? sheet.Images.Count : 0;
            int shapeCount = sheet.Shapes != null ? sheet.Shapes.Count : 0;
            int diffCount = _sheetDiffs != null ? _sheetDiffs.Count : 0;

            HeaderText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} · シート「{1}」 · 差分 {2} 件 · セル{3} / 表{4} / 画像{5} / 図形{6}",
                side,
                sheet.Name ?? "（無名）",
                diffCount,
                cellCount,
                tableCount,
                imageCount,
                shapeCount);

            LoadCellsTab(sheet);
            LoadTablesTab(sheet, partnerSheet);
            LoadImagesTab(sheet, partnerSheet);
            LoadShapesTab(sheet);
        }

        /// <summary>
        /// セルタブ（テーブル外セル＋関連差分のプレースホルダ）。
        /// </summary>
        private void LoadCellsTab(SheetContent sheet)
        {
            var lines = new List<string>();
            int loose = sheet.LooseCells != null ? sheet.LooseCells.Count : 0;
            CellsSummary.Text = string.Format(
                CultureInfo.InvariantCulture,
                "セル（テーブル外）: {0} 件 · 関連差分 {1} 件",
                loose,
                CountDiffs(DiffKind.Text, DiffKind.Background));

            if (sheet.LooseCells != null)
            {
                int shown = 0;
                foreach (CellContent cell in sheet.LooseCells)
                {
                    if (cell == null)
                    {
                        continue;
                    }

                    lines.Add(FormatCellLine(cell));
                    shown++;
                    if (shown >= 200)
                    {
                        lines.Add("… 以降省略（最大 200 件表示）");
                        break;
                    }
                }
            }

            foreach (DiffItem d in EnumerateDiffs(DiffKind.Text, DiffKind.Background))
            {
                lines.Add(FormatDiffLine(d));
            }

            if (lines.Count == 0)
            {
                lines.Add("（セルなし）");
            }

            CellsList.ItemsSource = lines;
        }

        /// <summary>
        /// テーブルタブ: 対応テーブルごとに TableDiffGrid を配置する。
        /// </summary>
        private void LoadTablesTab(SheetContent sheet, SheetContent partnerSheet)
        {
            ClearTablesHost();

            IList<TableBlock> selfTables =
                sheet != null && sheet.Tables != null
                    ? (IList<TableBlock>)sheet.Tables
                    : (IList<TableBlock>)Array.Empty<TableBlock>();
            IList<TableBlock> partnerTables =
                partnerSheet != null && partnerSheet.Tables != null
                    ? (IList<TableBlock>)partnerSheet.Tables
                    : (IList<TableBlock>)Array.Empty<TableBlock>();

            IList<TableBlock> leftTables = _isLeft ? selfTables : partnerTables;
            IList<TableBlock> rightTables = _isLeft ? partnerTables : selfTables;

            int tableDiffCount = CountDiffs(
                DiffKind.TableRowDelete,
                DiffKind.TableRowInsert,
                DiffKind.TableCellChange);

            TablesSummary.Text = string.Format(
                CultureInfo.InvariantCulture,
                "テーブル: 自側 {0} / 相手 {1} · 関連差分 {2} 件（行削除=赤 · 行追加=緑 · 相手欠落=空行 · セル変更=黄）",
                selfTables.Count,
                partnerTables.Count,
                tableDiffCount);

            if (leftTables.Count == 0 && rightTables.Count == 0)
            {
                TablesHost.Children.Add(CreatePlainHint("（テーブルなし）"));
                return;
            }

            IList<AlignStep> tableSteps = SequenceAligner.Align(
                leftTables.Count,
                rightTables.Count,
                (i, j) => TableSimilarity(leftTables[i], rightTables[j]),
                TableMatchThreshold,
                TableSkipCost);

            int grids = 0;
            foreach (AlignStep step in tableSteps)
            {
                if (step == null)
                {
                    continue;
                }

                TableBlock leftT = null;
                TableBlock rightT = null;
                if (step.Op == AlignOp.Match
                    || step.Op == AlignOp.SkipLeft)
                {
                    if (step.LeftIndex >= 0 && step.LeftIndex < leftTables.Count)
                    {
                        leftT = leftTables[step.LeftIndex];
                    }
                }

                if (step.Op == AlignOp.Match
                    || step.Op == AlignOp.SkipRight)
                {
                    if (step.RightIndex >= 0 && step.RightIndex < rightTables.Count)
                    {
                        rightT = rightTables[step.RightIndex];
                    }
                }

                // 片側のみテーブルでもギャップ行を出す（left/right どちらかがあれば表示）
                if (leftT == null && rightT == null)
                {
                    continue;
                }

                IList<DiffItem> tableDiffs = FilterDiffsForTable(leftT, rightT);
                var grid = new TableDiffGrid();
                grid.Load(leftT, rightT, tableDiffs, _isLeft);
                TablesHost.Children.Add(grid);
                grids++;
            }

            if (grids == 0)
            {
                TablesHost.Children.Add(CreatePlainHint("（表示するテーブルなし）"));
            }
        }

        /// <summary>
        /// 画像タブ: AlignStep 順の ImagePairView を配置する。
        /// Match は部分差領域を赤 3px＋黄 50% で重ね、Skip は片側のみ／ギャップ。
        /// </summary>
        private void LoadImagesTab(SheetContent sheet, SheetContent partnerSheet)
        {
            ClearImagesHost();

            IList<EmbeddedImage> selfImages =
                sheet != null && sheet.Images != null
                    ? (IList<EmbeddedImage>)sheet.Images
                    : (IList<EmbeddedImage>)Array.Empty<EmbeddedImage>();
            IList<EmbeddedImage> partnerImages =
                partnerSheet != null && partnerSheet.Images != null
                    ? (IList<EmbeddedImage>)partnerSheet.Images
                    : (IList<EmbeddedImage>)Array.Empty<EmbeddedImage>();

            IList<EmbeddedImage> leftImages = _isLeft ? selfImages : partnerImages;
            IList<EmbeddedImage> rightImages = _isLeft ? partnerImages : selfImages;

            int imageDiffCount = CountDiffs(
                DiffKind.Image, DiffKind.ImageOnlyLeft, DiffKind.ImageOnlyRight);
            int regionTotal = 0;
            foreach (DiffItem d in EnumerateDiffs(DiffKind.Image))
            {
                if (d != null && d.HighlightRegions != null)
                {
                    regionTotal += d.HighlightRegions.Count;
                }
            }

            ImagesSummary.Text = string.Format(
                CultureInfo.InvariantCulture,
                "画像: 自側 {0} / 相手 {1} · 関連差分 {2} 件 · 領域 {3} · ハイライト {4}（赤枠3px＋黄50%・トグル可）",
                selfImages.Count,
                partnerImages.Count,
                imageDiffCount,
                regionTotal,
                _highlightVisible ? "ON" : "OFF");

            if (leftImages.Count == 0 && rightImages.Count == 0)
            {
                ImagesHost.Children.Add(CreatePlainHint("（画像なし）"));
                return;
            }

            IList<AlignStep> steps;
            try
            {
                steps = ImageSequenceAligner.Align(leftImages, rightImages);
            }
            catch
            {
                // アライン失敗時は自側を単純列挙
                steps = BuildFallbackImageSteps(leftImages.Count, rightImages.Count, _isLeft);
            }

            int pairs = 0;
            foreach (AlignStep step in steps)
            {
                if (step == null)
                {
                    continue;
                }

                EmbeddedImage leftImg = null;
                EmbeddedImage rightImg = null;
                if (step.Op == AlignOp.Match || step.Op == AlignOp.SkipLeft)
                {
                    if (step.LeftIndex >= 0 && step.LeftIndex < leftImages.Count)
                    {
                        leftImg = leftImages[step.LeftIndex];
                    }
                }

                if (step.Op == AlignOp.Match || step.Op == AlignOp.SkipRight)
                {
                    if (step.RightIndex >= 0 && step.RightIndex < rightImages.Count)
                    {
                        rightImg = rightImages[step.RightIndex];
                    }
                }

                if (leftImg == null && rightImg == null)
                {
                    continue;
                }

                DiffItem related = FindImageDiff(leftImg, rightImg, step.Op);
                var view = new ImagePairView();
                view.Load(leftImg, rightImg, related, _isLeft, _highlightVisible);
                ImagesHost.Children.Add(view);
                _imagePairViews.Add(view);
                pairs++;
            }

            if (pairs == 0)
            {
                ImagesHost.Children.Add(CreatePlainHint("（表示する画像なし）"));
            }
        }

        /// <summary>
        /// 画像ホストを空にする。
        /// </summary>
        private void ClearImagesHost()
        {
            _imagePairViews.Clear();
            if (ImagesHost != null)
            {
                ImagesHost.Children.Clear();
            }
        }

        /// <summary>
        /// Align 失敗時のフォールバック AlignStep 列。
        /// </summary>
        private static IList<AlignStep> BuildFallbackImageSteps(int leftCount, int rightCount, bool isLeft)
        {
            var steps = new List<AlignStep>();
            if (isLeft)
            {
                for (int i = 0; i < leftCount; i++)
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
                for (int j = 0; j < rightCount; j++)
                {
                    steps.Add(new AlignStep
                    {
                        Op = AlignOp.SkipRight,
                        LeftIndex = -1,
                        RightIndex = j
                    });
                }
            }

            return steps;
        }

        /// <summary>
        /// 画像ペア／片側に対応する DiffItem を探す。
        /// </summary>
        private DiffItem FindImageDiff(
            EmbeddedImage leftImg,
            EmbeddedImage rightImg,
            AlignOp op)
        {
            if (_sheetDiffs == null)
            {
                return null;
            }

            string leftPath = leftImg != null ? leftImg.ExtractedPath : null;
            string rightPath = rightImg != null ? rightImg.ExtractedPath : null;

            foreach (DiffItem d in _sheetDiffs)
            {
                if (d == null)
                {
                    continue;
                }

                if (op == AlignOp.Match && d.Kind == DiffKind.Image)
                {
                    if (PathsEqual(d.LeftImagePath, leftPath)
                        && PathsEqual(d.RightImagePath, rightPath))
                    {
                        return d;
                    }

                    // パス不一致でも片側パスが一致すれば候補
                    if (!string.IsNullOrEmpty(leftPath)
                        && PathsEqual(d.LeftImagePath, leftPath))
                    {
                        return d;
                    }

                    if (!string.IsNullOrEmpty(rightPath)
                        && PathsEqual(d.RightImagePath, rightPath))
                    {
                        return d;
                    }
                }
                else if (op == AlignOp.SkipLeft && d.Kind == DiffKind.ImageOnlyLeft)
                {
                    if (PathsEqual(d.LeftImagePath, leftPath))
                    {
                        return d;
                    }
                }
                else if (op == AlignOp.SkipRight && d.Kind == DiffKind.ImageOnlyRight)
                {
                    if (PathsEqual(d.RightImagePath, rightPath))
                    {
                        return d;
                    }
                }
            }

            // DiffItem が無い完全一致 Match は null（ハイライトなし）
            if (op == AlignOp.SkipLeft)
            {
                return new DiffItem
                {
                    Kind = DiffKind.ImageOnlyLeft,
                    LeftImagePath = leftPath,
                    Summary = "左のみの画像"
                };
            }

            if (op == AlignOp.SkipRight)
            {
                return new DiffItem
                {
                    Kind = DiffKind.ImageOnlyRight,
                    RightImagePath = rightPath,
                    Summary = "右のみの画像"
                };
            }

            return null;
        }

        /// <summary>
        /// 画像パスの緩い一致（null 同士は false）。
        /// </summary>
        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 図形タブのプレースホルダ。
        /// </summary>
        private void LoadShapesTab(SheetContent sheet)
        {
            var lines = new List<string>();
            int n = sheet.Shapes != null ? sheet.Shapes.Count : 0;
            ShapesSummary.Text = string.Format(
                CultureInfo.InvariantCulture,
                "図形: {0} 件 · 関連差分 {1} 件",
                n,
                CountDiffs(DiffKind.Shape, DiffKind.ShapeOnlyLeft, DiffKind.ShapeOnlyRight));

            if (sheet.Shapes != null)
            {
                foreach (ShapeContent s in sheet.Shapes)
                {
                    if (s == null)
                    {
                        continue;
                    }

                    string text = s.Text;
                    if (!string.IsNullOrEmpty(text) && text.Length > 40)
                    {
                        text = text.Substring(0, 40) + "…";
                    }

                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0}] #{1} kind={2} text={3} hash={4}",
                        s.Id ?? "?",
                        s.OrderIndex,
                        s.Kind ?? "?",
                        string.IsNullOrEmpty(text) ? "—" : text,
                        ShortHash(s.ContentHash)));
                }
            }

            foreach (DiffItem d in EnumerateDiffs(
                DiffKind.Shape, DiffKind.ShapeOnlyLeft, DiffKind.ShapeOnlyRight))
            {
                lines.Add(FormatDiffLine(d));
            }

            if (lines.Count == 0)
            {
                lines.Add("（図形なし）");
            }

            ShapesList.ItemsSource = lines;
        }

        /// <summary>
        /// テーブルホストを空にする。
        /// </summary>
        private void ClearTablesHost()
        {
            if (TablesHost != null)
            {
                TablesHost.Children.Clear();
            }
        }

        /// <summary>
        /// プレーンなヒント TextBlock を作る。
        /// </summary>
        private static TextBlock CreatePlainHint(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF)),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            };
        }

        /// <summary>
        /// テーブル ID に紐づく差分を抽出する。
        /// </summary>
        private IList<DiffItem> FilterDiffsForTable(TableBlock leftT, TableBlock rightT)
        {
            string idL = leftT != null ? leftT.Id : null;
            string idR = rightT != null ? rightT.Id : null;
            var list = new List<DiffItem>();
            if (_sheetDiffs == null)
            {
                return list;
            }

            foreach (DiffItem d in _sheetDiffs)
            {
                if (d == null)
                {
                    continue;
                }

                if (d.Kind != DiffKind.TableRowDelete
                    && d.Kind != DiffKind.TableRowInsert
                    && d.Kind != DiffKind.TableCellChange)
                {
                    continue;
                }

                bool matchL = !string.IsNullOrEmpty(idL)
                    && string.Equals(d.TableIdLeft, idL, StringComparison.Ordinal);
                bool matchR = !string.IsNullOrEmpty(idR)
                    && string.Equals(d.TableIdRight, idR, StringComparison.Ordinal);
                if (matchL || matchR)
                {
                    list.Add(d);
                }
            }

            return list;
        }

        /// <summary>
        /// 2 テーブルの粗類似度（行キー多重集合 Jaccard）。TableCompareService と同趣旨。
        /// </summary>
        private static double TableSimilarity(TableBlock left, TableBlock right)
        {
            List<string> leftKeys = CollectRowKeys(left);
            List<string> rightKeys = CollectRowKeys(right);

            if (leftKeys.Count == 0 && rightKeys.Count == 0)
            {
                return 1.0;
            }

            if (leftKeys.Count == 0 || rightKeys.Count == 0)
            {
                return 0.0;
            }

            var leftCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string k in leftKeys)
            {
                int c;
                leftCount.TryGetValue(k, out c);
                leftCount[k] = c + 1;
            }

            var rightCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string k in rightKeys)
            {
                int c;
                rightCount.TryGetValue(k, out c);
                rightCount[k] = c + 1;
            }

            int inter = 0;
            int union = 0;
            var allKeys = new HashSet<string>(leftCount.Keys, StringComparer.Ordinal);
            foreach (string k in rightCount.Keys)
            {
                allKeys.Add(k);
            }

            foreach (string k in allKeys)
            {
                int lc;
                int rc;
                leftCount.TryGetValue(k, out lc);
                rightCount.TryGetValue(k, out rc);
                inter += Math.Min(lc, rc);
                union += Math.Max(lc, rc);
            }

            if (union == 0)
            {
                return 1.0;
            }

            return (double)inter / union;
        }

        /// <summary>
        /// テーブルの行キー一覧。
        /// </summary>
        private static List<string> CollectRowKeys(TableBlock table)
        {
            var keys = new List<string>();
            if (table == null || table.Rows == null)
            {
                return keys;
            }

            for (int i = 0; i < table.Rows.Count; i++)
            {
                keys.Add(TableRowAligner.MakeRowKey(table.Rows[i]));
            }

            return keys;
        }

        /// <summary>
        /// 指定 Kind の差分件数。
        /// </summary>
        private int CountDiffs(params DiffKind[] kinds)
        {
            if (_sheetDiffs == null || kinds == null || kinds.Length == 0)
            {
                return 0;
            }

            return _sheetDiffs.Count(d => d != null && kinds.Contains(d.Kind));
        }

        /// <summary>
        /// 指定 Kind の差分を列挙する。
        /// </summary>
        private IEnumerable<DiffItem> EnumerateDiffs(params DiffKind[] kinds)
        {
            if (_sheetDiffs == null || kinds == null)
            {
                yield break;
            }

            foreach (DiffItem d in _sheetDiffs)
            {
                if (d != null && kinds.Contains(d.Kind))
                {
                    yield return d;
                }
            }
        }

        /// <summary>
        /// セル 1 行の表示文字列。
        /// </summary>
        private static string FormatCellLine(CellContent cell)
        {
            string bg = string.IsNullOrEmpty(cell.BackgroundArgb) ? "—" : cell.BackgroundArgb;
            string text = cell.Text ?? string.Empty;
            if (text.Length > 60)
            {
                text = text.Substring(0, 60) + "…";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}  \"{1}\"  bg={2}{3}",
                cell.Address ?? ("R" + cell.Row + "C" + cell.Column),
                text,
                bg,
                cell.HasAnyBorder ? "  border" : string.Empty);
        }

        /// <summary>
        /// 差分 1 行の表示文字列。
        /// </summary>
        private string FormatDiffLine(DiffItem d)
        {
            string addr = _isLeft
                ? (d.AddressLeft ?? d.AddressRight)
                : (d.AddressRight ?? d.AddressLeft);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Δ {0}  {1}  {2}",
                d.Kind,
                addr ?? string.Empty,
                d.Summary ?? string.Empty);
        }

        /// <summary>
        /// ハッシュの短縮表示。
        /// </summary>
        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "—";
            }

            return hash.Length <= 12 ? hash : hash.Substring(0, 12) + "…";
        }
    }
}
