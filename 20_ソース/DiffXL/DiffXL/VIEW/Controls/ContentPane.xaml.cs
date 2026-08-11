using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        /// 現在表示中のシート。
        /// </summary>
        private SheetContent _sheet;

        /// <summary>
        /// 現在シートに紐づく差分。
        /// </summary>
        private IList<DiffItem> _sheetDiffs = new List<DiffItem>();

        /// <summary>
        /// 左ペインかどうか。
        /// </summary>
        private bool _isLeft = true;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public ContentPane()
        {
            InitializeComponent();
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
            _sheet = sheet;
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
                TablesList.ItemsSource = null;
                ImagesList.ItemsSource = null;
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
            LoadTablesTab(sheet);
            LoadImagesTab(sheet);
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
        /// テーブルタブのプレースホルダ。
        /// </summary>
        private void LoadTablesTab(SheetContent sheet)
        {
            var lines = new List<string>();
            int n = sheet.Tables != null ? sheet.Tables.Count : 0;
            TablesSummary.Text = string.Format(
                CultureInfo.InvariantCulture,
                "テーブル: {0} 件 · 関連差分 {1} 件",
                n,
                CountDiffs(DiffKind.TableRowDelete, DiffKind.TableRowInsert, DiffKind.TableCellChange));

            if (sheet.Tables != null)
            {
                foreach (TableBlock t in sheet.Tables)
                {
                    if (t == null)
                    {
                        continue;
                    }

                    int rows = t.Rows != null ? t.Rows.Count : 0;
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0}] order={1} R{2}-{3} C{4}-{5} 行数={6}",
                        t.Id ?? "?",
                        t.OrderIndex,
                        t.RowStart,
                        t.RowEnd,
                        t.ColStart,
                        t.ColEnd,
                        rows));
                }
            }

            foreach (DiffItem d in EnumerateDiffs(
                DiffKind.TableRowDelete, DiffKind.TableRowInsert, DiffKind.TableCellChange))
            {
                lines.Add(FormatDiffLine(d));
            }

            if (lines.Count == 0)
            {
                lines.Add("（テーブルなし）");
            }

            TablesList.ItemsSource = lines;
        }

        /// <summary>
        /// 画像タブのプレースホルダ。
        /// </summary>
        private void LoadImagesTab(SheetContent sheet)
        {
            var lines = new List<string>();
            int n = sheet.Images != null ? sheet.Images.Count : 0;
            ImagesSummary.Text = string.Format(
                CultureInfo.InvariantCulture,
                "画像: {0} 件 · 関連差分 {1} 件",
                n,
                CountDiffs(DiffKind.Image, DiffKind.ImageOnlyLeft, DiffKind.ImageOnlyRight));

            if (sheet.Images != null)
            {
                int i = 0;
                foreach (EmbeddedImage img in sheet.Images)
                {
                    if (img == null)
                    {
                        continue;
                    }

                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "#{0} {1} {2}x{3} hash={4}",
                        i,
                        img.FileName ?? img.PackagePath ?? "?",
                        img.PixelWidth,
                        img.PixelHeight,
                        ShortHash(img.ContentHash)));
                    i++;
                }
            }

            foreach (DiffItem d in EnumerateDiffs(
                DiffKind.Image, DiffKind.ImageOnlyLeft, DiffKind.ImageOnlyRight))
            {
                lines.Add(FormatDiffLine(d));
            }

            if (lines.Count == 0)
            {
                lines.Add("（画像なし）");
            }

            ImagesList.ItemsSource = lines;
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
