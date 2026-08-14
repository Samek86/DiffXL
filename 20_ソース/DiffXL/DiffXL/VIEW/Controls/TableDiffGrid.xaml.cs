using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// テーブル行差分グリッド。左右 TableBlock を行アラインし、
    /// 削除・挿入・セル変更を背景色で示す（片側ペイン用）。
    /// </summary>
    public partial class TableDiffGrid : UserControl
    {
        /// <summary>
        /// 削除行（自側に内容あり）の背景。
        /// </summary>
        private static readonly SolidColorBrush BrushDeleteRow =
            CreateFrozenBrush(0xFF, 0xFE, 0xE2, 0xE2);

        /// <summary>
        /// 削除行の枠。
        /// </summary>
        private static readonly SolidColorBrush BrushDeleteBorder =
            CreateFrozenBrush(0xFF, 0xF8, 0x71, 0x71);

        /// <summary>
        /// 相手側削除により自側が空く行の背景。
        /// </summary>
        private static readonly SolidColorBrush BrushDeleteGap =
            CreateFrozenBrush(0xFF, 0xFE, 0xF2, 0xF2);

        /// <summary>
        /// 挿入行（自側に内容あり）の背景。
        /// </summary>
        private static readonly SolidColorBrush BrushInsertRow =
            CreateFrozenBrush(0xFF, 0xD1, 0xFA, 0xE5);

        /// <summary>
        /// 挿入行の枠。
        /// </summary>
        private static readonly SolidColorBrush BrushInsertBorder =
            CreateFrozenBrush(0xFF, 0x34, 0xD3, 0x99);

        /// <summary>
        /// 相手側挿入により自側が空く行の背景。
        /// </summary>
        private static readonly SolidColorBrush BrushInsertGap =
            CreateFrozenBrush(0xFF, 0xEC, 0xFD, 0xF5);

        /// <summary>
        /// 一致行の背景。
        /// </summary>
        private static readonly SolidColorBrush BrushMatchRow =
            CreateFrozenBrush(0xFF, 0xFF, 0xFF, 0xFF);

        /// <summary>
        /// 一致行の枠。
        /// </summary>
        private static readonly SolidColorBrush BrushMatchBorder =
            CreateFrozenBrush(0xFF, 0xE5, 0xE7, 0xEB);

        /// <summary>
        /// 通常セル背景。
        /// </summary>
        private static readonly SolidColorBrush BrushCellNormal =
            CreateFrozenBrush(0xFF, 0xF9, 0xFA, 0xFB);

        /// <summary>
        /// 空ギャップセル背景。
        /// </summary>
        private static readonly SolidColorBrush BrushCellEmpty =
            CreateFrozenBrush(0xFF, 0xF3, 0xF4, 0xF6);

        /// <summary>
        /// マーカー・通常テキスト色。
        /// </summary>
        private static readonly SolidColorBrush BrushText =
            CreateFrozenBrush(0xFF, 0x11, 0x18, 0x27);

        /// <summary>
        /// 削除マーカー色。
        /// </summary>
        private static readonly SolidColorBrush BrushMarkerDelete =
            CreateFrozenBrush(0xFF, 0xB9, 0x1C, 0x1C);

        /// <summary>
        /// 挿入マーカー色。
        /// </summary>
        private static readonly SolidColorBrush BrushMarkerInsert =
            CreateFrozenBrush(0xFF, 0x04, 0x78, 0x57);

        /// <summary>
        /// ギャップマーカー色。
        /// </summary>
        private static readonly SolidColorBrush BrushMarkerGap =
            CreateFrozenBrush(0xFF, 0x6B, 0x72, 0x80);

        /// <summary>
        /// セル黄ハイライト表示。
        /// </summary>
        private bool _highlightVisible = true;

        /// <summary>
        /// 表示中の行 VM（トグル用）。
        /// </summary>
        private List<TableDiffRowVm> _rows;

        /// <summary>
        /// 変更セル用ブラシ。
        /// </summary>
        private Brush _changeBrush;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public TableDiffGrid()
        {
            InitializeComponent();
        }

        /// <summary>
        /// セル黄ハイライトの表示／非表示（再 Load なし）。
        /// </summary>
        public void SetHighlightVisible(bool visible)
        {
            _highlightVisible = visible;
            if (_rows == null)
            {
                return;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                TableDiffRowVm row = _rows[i];
                if (row != null)
                {
                    row.ApplyHighlightVisible(visible);
                }
            }
        }

        /// <summary>
        /// 左右テーブルと差分を読み込み、このペイン側の整列行を表示する。
        /// </summary>
        public void Load(
            TableBlock leftTable,
            TableBlock rightTable,
            IList<DiffItem> tableDiffs,
            bool isLeft)
        {
            Load(leftTable, rightTable, tableDiffs, isLeft, highlightVisible: true);
        }

        /// <summary>
        /// 左右テーブルと差分を読み込み、このペイン側の整列行を表示する。
        /// </summary>
        /// <param name="leftTable">左テーブル（null 可）</param>
        /// <param name="rightTable">右テーブル（null 可）</param>
        /// <param name="tableDiffs">当該テーブル関連の DiffItem（null 可）</param>
        /// <param name="isLeft">左ペインなら true</param>
        /// <param name="highlightVisible">セル黄ハイライトを出すか</param>
        public void Load(
            TableBlock leftTable,
            TableBlock rightTable,
            IList<DiffItem> tableDiffs,
            bool isLeft,
            bool highlightVisible)
        {
            _highlightVisible = highlightVisible;
            if (leftTable == null && rightTable == null)
            {
                TitleText.Text = "テーブルなし";
                SubtitleText.Text = string.Empty;
                RowsList.ItemsSource = null;
                _rows = null;
                EmptyHint.Visibility = Visibility.Visible;
                return;
            }

            TableBlock self = isLeft ? leftTable : rightTable;
            TableBlock partner = isLeft ? rightTable : leftTable;

            string selfId = self != null ? self.Id : null;
            string partnerId = partner != null ? partner.Id : null;
            string leftId = leftTable != null ? leftTable.Id : null;
            string rightId = rightTable != null ? rightTable.Id : null;

            TitleText.Text = BuildTitle(self, partner, isLeft, leftId, rightId);
            SubtitleText.Text = BuildSubtitle(tableDiffs, leftTable, rightTable);

            IList<IList<CellContent>> leftRows =
                leftTable != null && leftTable.Rows != null
                    ? leftTable.Rows
                    : Array.Empty<IList<CellContent>>();
            IList<IList<CellContent>> rightRows =
                rightTable != null && rightTable.Rows != null
                    ? rightTable.Rows
                    : Array.Empty<IList<CellContent>>();

            IList<AlignStep> steps = BuildRowSteps(leftTable, rightTable, leftRows, rightRows);
            int colCount = Math.Max(MaxColumnCount(leftRows), MaxColumnCount(rightRows));
            if (colCount < 1)
            {
                colCount = 1;
            }

            _changeBrush = CreateChangeCellBrush();
            var rows = new List<TableDiffRowVm>();

            foreach (AlignStep step in steps)
            {
                if (step == null)
                {
                    continue;
                }

                if (step.Op == AlignOp.Match)
                {
                    IList<CellContent> lrow =
                        step.LeftIndex >= 0 && step.LeftIndex < leftRows.Count
                            ? leftRows[step.LeftIndex]
                            : null;
                    IList<CellContent> rrow =
                        step.RightIndex >= 0 && step.RightIndex < rightRows.Count
                            ? rightRows[step.RightIndex]
                            : null;
                    IList<CellContent> show = isLeft ? lrow : rrow;
                    rows.Add(BuildMatchRow(show, lrow, rrow, colCount, isLeft, _changeBrush, _highlightVisible));
                }
                else if (step.Op == AlignOp.SkipLeft)
                {
                    IList<CellContent> lrow =
                        step.LeftIndex >= 0 && step.LeftIndex < leftRows.Count
                            ? leftRows[step.LeftIndex]
                            : null;
                    if (isLeft)
                    {
                        rows.Add(BuildContentRow(
                            lrow,
                            colCount,
                            "− 削除",
                            BrushMarkerDelete,
                            BrushDeleteRow,
                            BrushDeleteBorder,
                            "左のみの行（削除）",
                            isGap: false));
                    }
                    else
                    {
                        // 右ペイン: 左にあった行が無い → 空白で欠落を明示
                        rows.Add(BuildContentRow(
                            null,
                            Math.Max(colCount, lrow != null ? lrow.Count : 0),
                            "∅ 欠落",
                            BrushMarkerGap,
                            BrushDeleteGap,
                            BrushDeleteBorder,
                            "左にのみ存在する行（この側は空）",
                            isGap: true));
                    }
                }
                else if (step.Op == AlignOp.SkipRight)
                {
                    IList<CellContent> rrow =
                        step.RightIndex >= 0 && step.RightIndex < rightRows.Count
                            ? rightRows[step.RightIndex]
                            : null;
                    if (!isLeft)
                    {
                        rows.Add(BuildContentRow(
                            rrow,
                            colCount,
                            "+ 追加",
                            BrushMarkerInsert,
                            BrushInsertRow,
                            BrushInsertBorder,
                            "右のみの行（追加）",
                            isGap: false));
                    }
                    else
                    {
                        // 左ペイン: 右に追加された行 → 空白ギャップ
                        rows.Add(BuildContentRow(
                            null,
                            Math.Max(colCount, rrow != null ? rrow.Count : 0),
                            "∅ 追加先",
                            BrushMarkerGap,
                            BrushInsertGap,
                            BrushInsertBorder,
                            "右にのみ存在する行（この側は空）",
                            isGap: true));
                    }
                }
            }

            _rows = rows;
            RowsList.ItemsSource = rows;
            EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 行アライン手順を構築する。
        /// </summary>
        private static IList<AlignStep> BuildRowSteps(
            TableBlock leftTable,
            TableBlock rightTable,
            IList<IList<CellContent>> leftRows,
            IList<IList<CellContent>> rightRows)
        {
            if (leftTable != null && rightTable != null)
            {
                return TableRowAligner.AlignRows(leftRows, rightRows);
            }

            var steps = new List<AlignStep>();
            if (leftTable != null)
            {
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
            else if (rightTable != null)
            {
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

            return steps;
        }

        /// <summary>
        /// Match 行の VM を構築する（変更セルを黄強調。トグル可能）。
        /// </summary>
        private static TableDiffRowVm BuildMatchRow(
            IList<CellContent> showRow,
            IList<CellContent> leftRow,
            IList<CellContent> rightRow,
            int colCount,
            bool isLeft,
            Brush changeBrush,
            bool highlightVisible)
        {
            int n = Math.Max(colCount, showRow != null ? showRow.Count : 0);
            if (n < 1)
            {
                n = 1;
            }

            Brush changeMarker = CreateFrozenBrush(0xFF, 0xFD, 0xE0, 0x47);
            Brush changeBorder = CreateFrozenBrush(0xFF, 0xCA, 0x8A, 0x04);
            var cells = new List<TableDiffCellVm>(n);
            bool anyChange = false;
            for (int c = 0; c < n; c++)
            {
                CellContent sc = GetCell(showRow, c);
                CellContent lc = GetCell(leftRow, c);
                CellContent rc = GetCell(rightRow, c);
                bool changed = IsCellChanged(lc, rc);
                if (changed)
                {
                    anyChange = true;
                }

                string text = sc != null && sc.Text != null ? sc.Text : string.Empty;
                string addr = sc != null ? sc.Address : null;
                string tip = BuildCellToolTip(addr, text, changed, isLeft, lc, rc);

                cells.Add(new TableDiffCellVm
                {
                    Text = string.IsNullOrEmpty(text) ? " " : text,
                    IsChanged = changed,
                    HighlightBrush = changeBrush,
                    NormalBrush = BrushCellNormal,
                    CellBackground = changed && highlightVisible ? changeBrush : BrushCellNormal,
                    TextForeground = BrushText,
                    ToolTip = tip
                });
            }

            var row = new TableDiffRowVm
            {
                Marker = anyChange ? "± 変更" : "＝",
                HasCellChanges = anyChange,
                HighlightMarkerBrush = changeMarker,
                NormalMarkerBrush = BrushMarkerGap,
                HighlightBorderBrush = changeBorder,
                NormalBorderBrush = BrushMatchBorder,
                MarkerForeground = anyChange && highlightVisible ? changeMarker : BrushMarkerGap,
                RowBackground = BrushMatchRow,
                RowBorder = anyChange && highlightVisible ? changeBorder : BrushMatchBorder,
                RowToolTip = anyChange ? "対応行内にセル差分あり" : "対応行（一致）",
                Cells = cells
            };
            return row;
        }

        /// <summary>
        /// 削除・挿入・ギャップ行の VM を構築する。
        /// </summary>
        private static TableDiffRowVm BuildContentRow(
            IList<CellContent> row,
            int colCount,
            string marker,
            Brush markerFg,
            Brush rowBg,
            Brush rowBorder,
            string rowTip,
            bool isGap)
        {
            int n = Math.Max(colCount, row != null ? row.Count : 0);
            if (n < 1)
            {
                n = 1;
            }

            var cells = new List<TableDiffCellVm>(n);
            for (int c = 0; c < n; c++)
            {
                if (isGap || row == null)
                {
                    cells.Add(new TableDiffCellVm
                    {
                        Text = "·",
                        CellBackground = BrushCellEmpty,
                        TextForeground = BrushMarkerGap,
                        ToolTip = rowTip
                    });
                    continue;
                }

                CellContent cell = GetCell(row, c);
                string text = cell != null && cell.Text != null ? cell.Text : string.Empty;
                cells.Add(new TableDiffCellVm
                {
                    Text = string.IsNullOrEmpty(text) ? " " : text,
                    CellBackground = BrushCellNormal,
                    TextForeground = BrushText,
                    ToolTip = BuildCellToolTip(
                        cell != null ? cell.Address : null,
                        text,
                        changed: false,
                        isLeft: true,
                        left: cell,
                        right: null)
                });
            }

            return new TableDiffRowVm
            {
                Marker = marker,
                MarkerForeground = markerFg,
                RowBackground = rowBg,
                RowBorder = rowBorder,
                RowToolTip = rowTip,
                Cells = cells
            };
        }

        /// <summary>
        /// タイトル文字列。
        /// </summary>
        private static string BuildTitle(
            TableBlock self,
            TableBlock partner,
            bool isLeft,
            string leftId,
            string rightId)
        {
            string side = isLeft ? "左" : "右";
            string selfLabel = self != null
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] R{1}-{2} C{3}-{4}",
                    self.Id ?? "?",
                    self.RowStart,
                    self.RowEnd,
                    self.ColStart,
                    self.ColEnd)
                : "（なし）";
            string pairLabel = string.Format(
                CultureInfo.InvariantCulture,
                "対応 {0} ↔ {1}",
                leftId ?? "—",
                rightId ?? "—");
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} · {2}",
                side,
                selfLabel,
                pairLabel);
        }

        /// <summary>
        /// サブタイトル（差分件数など）。
        /// </summary>
        private static string BuildSubtitle(
            IList<DiffItem> tableDiffs,
            TableBlock leftTable,
            TableBlock rightTable)
        {
            int del = 0;
            int ins = 0;
            int chg = 0;
            if (tableDiffs != null)
            {
                foreach (DiffItem d in tableDiffs)
                {
                    if (d == null)
                    {
                        continue;
                    }

                    if (d.Kind == DiffKind.TableRowDelete)
                    {
                        del++;
                    }
                    else if (d.Kind == DiffKind.TableRowInsert)
                    {
                        ins++;
                    }
                    else if (d.Kind == DiffKind.TableCellChange)
                    {
                        chg++;
                    }
                }
            }

            int leftN = leftTable != null && leftTable.Rows != null ? leftTable.Rows.Count : 0;
            int rightN = rightTable != null && rightTable.Rows != null ? rightTable.Rows.Count : 0;
            return string.Format(
                CultureInfo.InvariantCulture,
                "行 左{0} / 右{1} · 削除{2} · 追加{3} · セル変更{4}",
                leftN,
                rightN,
                del,
                ins,
                chg);
        }

        /// <summary>
        /// セル変更判定（Text のみ。Bg の交互行塗りは差分にしない）。
        /// </summary>
        private static bool IsCellChanged(CellContent left, CellContent right)
        {
            string lt = left != null && left.Text != null ? left.Text : string.Empty;
            string rt = right != null && right.Text != null ? right.Text : string.Empty;
            return !string.Equals(lt, rt, StringComparison.Ordinal);
        }

        /// <summary>
        /// セルツールチップ。
        /// </summary>
        private static string BuildCellToolTip(
            string addr,
            string text,
            bool changed,
            bool isLeft,
            CellContent left,
            CellContent right)
        {
            string a = string.IsNullOrEmpty(addr) ? "—" : addr;
            if (!changed)
            {
                return a + "  \"" + (text ?? string.Empty) + "\"";
            }

            string lt = left != null && left.Text != null ? left.Text : string.Empty;
            string rt = right != null && right.Text != null ? right.Text : string.Empty;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}  変更 「{1}」→「{2}」",
                a,
                lt,
                rt);
        }

        /// <summary>
        /// 行からセルを安全に取得する。
        /// </summary>
        private static CellContent GetCell(IList<CellContent> row, int index)
        {
            if (row == null || index < 0 || index >= row.Count)
            {
                return null;
            }

            return row[index];
        }

        /// <summary>
        /// 最大列数。
        /// </summary>
        private static int MaxColumnCount(IList<IList<CellContent>> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return 0;
            }

            int max = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                IList<CellContent> r = rows[i];
                if (r != null && r.Count > max)
                {
                    max = r.Count;
                }
            }

            return max;
        }

        /// <summary>
        /// 設定のハイライト色で変更セルブラシを作る。
        /// </summary>
        private static Brush CreateChangeCellBrush()
        {
            try
            {
                DiffHighlightStyle style = DiffHighlightStyle.FromSettings();
                Color c = style.ToWpfColor();
                // 暗背景上で視認できるよう最低限のアルファを確保
                if (c.A < 0x40)
                {
                    c = Color.FromArgb(0x80, c.R, c.G, c.B);
                }

                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return CreateFrozenBrush(0x80, 0xFF, 0xFF, 0x00);
            }
        }

        /// <summary>
        /// 凍結ソリッドブラシを生成する。
        /// </summary>
        private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 1 表示行のビューモデル。
        /// </summary>
        private sealed class TableDiffRowVm : INotifyPropertyChanged
        {
            private Brush _markerForeground;
            private Brush _rowBorder;

            public event PropertyChangedEventHandler PropertyChanged;

            /// <summary>行頭マーカー（削除・追加など）。</summary>
            public string Marker { get; set; }

            /// <summary>セル変更行か（Match 内）。</summary>
            public bool HasCellChanges { get; set; }

            public Brush HighlightMarkerBrush { get; set; }
            public Brush NormalMarkerBrush { get; set; }
            public Brush HighlightBorderBrush { get; set; }
            public Brush NormalBorderBrush { get; set; }

            /// <summary>マーカー前景色。</summary>
            public Brush MarkerForeground
            {
                get { return _markerForeground; }
                set
                {
                    if (!ReferenceEquals(_markerForeground, value))
                    {
                        _markerForeground = value;
                        OnPropertyChanged("MarkerForeground");
                    }
                }
            }

            /// <summary>行背景。</summary>
            public Brush RowBackground { get; set; }

            /// <summary>行枠。</summary>
            public Brush RowBorder
            {
                get { return _rowBorder; }
                set
                {
                    if (!ReferenceEquals(_rowBorder, value))
                    {
                        _rowBorder = value;
                        OnPropertyChanged("RowBorder");
                    }
                }
            }

            /// <summary>行ツールチップ。</summary>
            public string RowToolTip { get; set; }

            /// <summary>セル一覧。</summary>
            public IList<TableDiffCellVm> Cells { get; set; }

            public void ApplyHighlightVisible(bool visible)
            {
                if (HasCellChanges)
                {
                    MarkerForeground = visible ? HighlightMarkerBrush : NormalMarkerBrush;
                    RowBorder = visible ? HighlightBorderBrush : NormalBorderBrush;
                }

                if (Cells == null)
                {
                    return;
                }

                for (int i = 0; i < Cells.Count; i++)
                {
                    TableDiffCellVm cell = Cells[i];
                    if (cell != null)
                    {
                        cell.ApplyHighlightVisible(visible);
                    }
                }
            }

            private void OnPropertyChanged(string name)
            {
                PropertyChangedEventHandler h = PropertyChanged;
                if (h != null)
                {
                    h(this, new PropertyChangedEventArgs(name));
                }
            }
        }

        /// <summary>
        /// 1 セルのビューモデル。
        /// </summary>
        private sealed class TableDiffCellVm : INotifyPropertyChanged
        {
            private Brush _cellBackground;

            public event PropertyChangedEventHandler PropertyChanged;

            /// <summary>表示テキスト。</summary>
            public string Text { get; set; }

            /// <summary>値差分セルか。</summary>
            public bool IsChanged { get; set; }

            public Brush HighlightBrush { get; set; }
            public Brush NormalBrush { get; set; }

            /// <summary>セル背景。</summary>
            public Brush CellBackground
            {
                get { return _cellBackground; }
                set
                {
                    if (!ReferenceEquals(_cellBackground, value))
                    {
                        _cellBackground = value;
                        OnPropertyChanged("CellBackground");
                    }
                }
            }

            /// <summary>文字色。</summary>
            public Brush TextForeground { get; set; }

            /// <summary>ツールチップ。</summary>
            public string ToolTip { get; set; }

            public void ApplyHighlightVisible(bool visible)
            {
                if (!IsChanged)
                {
                    return;
                }

                CellBackground = visible ? HighlightBrush : NormalBrush;
            }

            private void OnPropertyChanged(string name)
            {
                PropertyChangedEventHandler h = PropertyChanged;
                if (h != null)
                {
                    h(this, new PropertyChangedEventArgs(name));
                }
            }
        }
    }
}
