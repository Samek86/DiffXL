using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// シート内容をドキュメント順の統一ストリームで表示する（セル・表・画像・図形を 1 本に並べる）。
    /// 左右は同一アライン列を共有し、スクロール／ジャンプを同期可能にする。
    /// </summary>
    public partial class ContentPane : UserControl
    {
        /// <summary>
        /// 現在のアライン済みストリーム（左右共通の列）。
        /// </summary>
        private IList<ContentStreamPair> _pairs = new List<ContentStreamPair>();

        /// <summary>
        /// 各ペア行のホスト要素（ScrollIntoView 用）。
        /// </summary>
        private readonly List<FrameworkElement> _pairElements = new List<FrameworkElement>();

        /// <summary>
        /// 左ペインかどうか。
        /// </summary>
        private bool _isLeft = true;

        /// <summary>
        /// シート差分。
        /// </summary>
        private IList<DiffItem> _sheetDiffs = new List<DiffItem>();

        /// <summary>
        /// 画像ハイライト表示。
        /// </summary>
        private bool _highlightVisible = true;

        /// <summary>
        /// 画像ビュー一覧。
        /// </summary>
        private readonly List<ImagePairView> _imagePairViews = new List<ImagePairView>();

        /// <summary>
        /// スクロール同期中の再入防止。
        /// </summary>
        private bool _suppressScrollEvent;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public ContentPane()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 縦スクロール比率 0..1 が変化した（ユーザー操作）。
        /// </summary>
        public event Action<double> VerticalScrollRatioChanged;

        /// <summary>
        /// 画像ハイライト表示中か。
        /// </summary>
        public bool HighlightVisible
        {
            get { return _highlightVisible; }
        }

        /// <summary>
        /// アライン済みペア数。
        /// </summary>
        public int PairCount
        {
            get { return _pairs != null ? _pairs.Count : 0; }
        }

        /// <summary>
        /// 現在のストリームペア（読み取り用）。
        /// </summary>
        public IList<ContentStreamPair> Pairs
        {
            get { return _pairs; }
        }

        /// <summary>
        /// 表示中シート名。
        /// </summary>
        public string SheetName { get; private set; }

        /// <summary>
        /// 画像ハイライト表示を伝播する。
        /// </summary>
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
        /// 設定の画像ハイライト色を再適用する。
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
        /// 互換: 旧 API（相手シートなし）。
        /// </summary>
        public void Load(SheetContent sheet, IList<DiffItem> sheetDiffs, bool isLeft)
        {
            Load(sheet, sheetDiffs, isLeft, partnerSheet: null);
        }

        /// <summary>
        /// シート内容を統一ストリームとして読み込む。
        /// 左右は絶対の left/right としてアラインし、自側だけ描画する。
        /// </summary>
        /// <param name="sheet">自側シート</param>
        /// <param name="sheetDiffs">差分</param>
        /// <param name="isLeft">左ペインか</param>
        /// <param name="partnerSheet">相手シート</param>
        public void Load(
            SheetContent sheet,
            IList<DiffItem> sheetDiffs,
            bool isLeft,
            SheetContent partnerSheet)
        {
            _isLeft = isLeft;
            _sheetDiffs = sheetDiffs ?? new List<DiffItem>();
            SheetName = sheet != null ? sheet.Name : null;

            SheetContent leftSheet = isLeft ? sheet : partnerSheet;
            SheetContent rightSheet = isLeft ? partnerSheet : sheet;

            IList<ContentStreamBlock> leftBlocks = ContentStreamBuilder.Build(leftSheet);
            IList<ContentStreamBlock> rightBlocks = ContentStreamBuilder.Build(rightSheet);
            _pairs = ContentStreamBuilder.Align(leftBlocks, rightBlocks);

            string side = isLeft ? "左" : "右";
            int selfBlocks = isLeft ? leftBlocks.Count : rightBlocks.Count;
            int diffCount = _sheetDiffs.Count;
            HeaderText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} · シート「{1}」 · ブロック {2} · 対応行 {3} · 差分 {4} 件（ドキュメント順・統一表示）",
                side,
                SheetName ?? "（なし）",
                selfBlocks,
                _pairs.Count,
                diffCount);

            RebuildStream();
        }

        /// <summary>
        /// 縦スクロール比率 0..1 を取得する。
        /// </summary>
        public double GetVerticalScrollRatio()
        {
            if (StreamScroll == null)
            {
                return 0;
            }

            double extent = StreamScroll.ScrollableHeight;
            if (extent <= 0.5)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(1, StreamScroll.VerticalOffset / extent));
        }

        /// <summary>
        /// 縦スクロール比率 0..1 を設定する（同期用。イベントは出さない）。
        /// </summary>
        public void SetVerticalScrollRatio(double ratio)
        {
            if (StreamScroll == null)
            {
                return;
            }

            double extent = StreamScroll.ScrollableHeight;
            if (extent <= 0.5)
            {
                return;
            }

            double target = Math.Max(0, Math.Min(1, ratio)) * extent;
            if (Math.Abs(StreamScroll.VerticalOffset - target) < 1.0)
            {
                return;
            }

            _suppressScrollEvent = true;
            try
            {
                StreamScroll.ScrollToVerticalOffset(target);
            }
            finally
            {
                _suppressScrollEvent = false;
            }
        }

        /// <summary>
        /// OrderHint に最も近いブロックへスクロールする（MiniMap 連携）。
        /// </summary>
        public bool ScrollToOrderHint(double orderHint)
        {
            int index = ContentStreamBuilder.FindNearestPairIndex(_pairs, orderHint);
            return ScrollToPairIndex(index);
        }

        /// <summary>
        /// ペア index の要素を表示領域へ持ってくる。
        /// </summary>
        public bool ScrollToPairIndex(int index)
        {
            if (index < 0 || index >= _pairElements.Count)
            {
                return false;
            }

            FrameworkElement el = _pairElements[index];
            if (el == null)
            {
                return false;
            }

            _suppressScrollEvent = true;
            try
            {
                el.BringIntoView();
            }
            finally
            {
                _suppressScrollEvent = false;
            }

            return true;
        }

        /// <summary>
        /// DiffItem の OrderHint / アドレス行からジャンプする。
        /// </summary>
        public bool ScrollToDiffItem(DiffItem item)
        {
            if (item == null)
            {
                return false;
            }

            if (item.OrderHint > 0)
            {
                return ScrollToOrderHint(item.OrderHint);
            }

            int row = TextDiffService.ParseAnchorRow(item.AddressLeft);
            if (row <= 0)
            {
                row = TextDiffService.ParseAnchorRow(item.AddressRight);
            }

            if (row > 0)
            {
                return ScrollToOrderHint(row * 1000.0);
            }

            return false;
        }

        /// <summary>
        /// ストリーム UI を再構築する。
        /// </summary>
        private void RebuildStream()
        {
            StreamHost.Children.Clear();
            _pairElements.Clear();
            _imagePairViews.Clear();

            if (_pairs == null || _pairs.Count == 0)
            {
                StreamHost.Children.Add(CreateHint("（表示する内容がありません）"));
                return;
            }

            for (int i = 0; i < _pairs.Count; i++)
            {
                ContentStreamPair pair = _pairs[i];
                if (pair == null)
                {
                    continue;
                }

                ContentStreamBlock self = _isLeft ? pair.Left : pair.Right;
                ContentStreamBlock partner = _isLeft ? pair.Right : pair.Left;
                bool isGap = self == null;

                FrameworkElement blockUi;
                if (isGap)
                {
                    blockUi = CreateGapBlock(pair, partner);
                }
                else
                {
                    blockUi = CreateBlockUi(pair, self, partner);
                }

                // 外側ラッパで index を保持
                var wrap = new Border
                {
                    Tag = i,
                    Margin = new Thickness(0, 0, 0, 10),
                    BorderBrush = isGap
                        ? new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB))
                        : new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Background = isGap
                        ? new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6))
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                    Child = blockUi
                };
                StreamHost.Children.Add(wrap);
                _pairElements.Add(wrap);
            }
        }

        /// <summary>
        /// 自側ブロックの UI を生成する。
        /// </summary>
        private FrameworkElement CreateBlockUi(
            ContentStreamPair pair,
            ContentStreamBlock self,
            ContentStreamBlock partner)
        {
            var panel = new StackPanel { Margin = new Thickness(8) };
            panel.Children.Add(CreateKindHeader(self, pair.Op));

            switch (self.Kind)
            {
                case ContentBlockKind.LooseRow:
                    panel.Children.Add(CreateLooseRowUi(self));
                    break;
                case ContentBlockKind.Table:
                    {
                        TableBlock leftT = _isLeft ? self.Table : (partner != null ? partner.Table : null);
                        TableBlock rightT = _isLeft ? (partner != null ? partner.Table : null) : self.Table;
                        // 片側欠落は partner null
                        if (!_isLeft)
                        {
                            leftT = partner != null ? partner.Table : null;
                            rightT = self.Table;
                        }
                        else
                        {
                            leftT = self.Table;
                            rightT = partner != null ? partner.Table : null;
                        }

                        IList<DiffItem> tableDiffs = FilterDiffsForTable(leftT, rightT);
                        var grid = new TableDiffGrid();
                        grid.Load(leftT, rightT, tableDiffs, _isLeft);
                        panel.Children.Add(grid);
                    }
                    break;
                case ContentBlockKind.Image:
                    {
                        EmbeddedImage leftImg = _isLeft ? self.Image : (partner != null ? partner.Image : null);
                        EmbeddedImage rightImg = _isLeft ? (partner != null ? partner.Image : null) : self.Image;
                        if (!_isLeft)
                        {
                            leftImg = partner != null ? partner.Image : null;
                            rightImg = self.Image;
                        }

                        DiffItem related = FindImageDiff(leftImg, rightImg, pair.Op);
                        var view = new ImagePairView();
                        view.Load(leftImg, rightImg, related, _isLeft, _highlightVisible);
                        _imagePairViews.Add(view);
                        panel.Children.Add(view);
                    }
                    break;
                case ContentBlockKind.Shape:
                    panel.Children.Add(CreateShapeUi(self));
                    break;
            }

            return panel;
        }

        /// <summary>
        /// 相手のみ存在する行のギャップ表示。
        /// </summary>
        private FrameworkElement CreateGapBlock(ContentStreamPair pair, ContentStreamBlock partner)
        {
            string kind = partner != null ? KindLabel(partner.Kind) : "内容";
            string detail = partner != null
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "相手側: {0} (行{1})",
                    kind,
                    partner.Row)
                : "相手側のみ";

            var panel = new StackPanel
            {
                Margin = new Thickness(12),
                MinHeight = 48
            };
            panel.Children.Add(new TextBlock
            {
                Text = "∅ この側になし（" + kind + "）",
                Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            });
            panel.Children.Add(new TextBlock
            {
                Text = detail + " · 対応を保つための空き行",
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            return panel;
        }

        private UIElement CreateKindHeader(ContentStreamBlock block, AlignOp op)
        {
            string mark = op == AlignOp.Match ? "＝" : (op == AlignOp.SkipLeft || op == AlignOp.SkipRight ? "±" : "·");
            string text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} · 行{2} 列{3}",
                mark,
                KindLabel(block.Kind),
                block.Row,
                block.Column);
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private static string KindLabel(ContentBlockKind kind)
        {
            switch (kind)
            {
                case ContentBlockKind.LooseRow:
                    return "セル行";
                case ContentBlockKind.Table:
                    return "テーブル";
                case ContentBlockKind.Image:
                    return "画像";
                case ContentBlockKind.Shape:
                    return "図形";
                default:
                    return kind.ToString();
            }
        }

        private UIElement CreateLooseRowUi(ContentStreamBlock block)
        {
            var panel = new StackPanel();
            if (block.Cells == null || block.Cells.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "（空行）",
                    Foreground = Brushes.Gray
                });
                return panel;
            }

            foreach (CellContent cell in block.Cells)
            {
                if (cell == null)
                {
                    continue;
                }

                string bg = string.IsNullOrEmpty(cell.BackgroundArgb) ? "" : " bg=" + cell.BackgroundArgb;
                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] {1}{2}",
                    cell.Address ?? "?",
                    cell.Text ?? string.Empty,
                    bg);
                bool isDiff = IsCellDiff(cell);
                panel.Children.Add(new Border
                {
                    Margin = new Thickness(0, 0, 0, 3),
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = isDiff
                        ? new SolidColorBrush(Color.FromArgb(0x80, 0xFE, 0xF0, 0x8A))
                        : new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Child = new TextBlock
                    {
                        Text = line,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                        FontFamily = new FontFamily("Consolas, Yu Gothic UI, sans-serif"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    }
                });
            }

            return panel;
        }

        private UIElement CreateShapeUi(ContentStreamBlock block)
        {
            ShapeContent s = block.Shape;
            string text = s == null
                ? "（図形）"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "図形 {0} · {1} · 「{2}」",
                    s.Kind ?? "?",
                    s.Id ?? "",
                    s.Text ?? "");
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
        }

        private bool IsCellDiff(CellContent cell)
        {
            if (cell == null || _sheetDiffs == null)
            {
                return false;
            }

            string addr = cell.Address ?? string.Empty;
            foreach (DiffItem d in _sheetDiffs)
            {
                if (d == null)
                {
                    continue;
                }

                if (d.Kind != DiffKind.Text && d.Kind != DiffKind.Background
                    && d.Kind != DiffKind.TableCellChange)
                {
                    continue;
                }

                if (string.Equals(d.AddressLeft, addr, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(d.AddressRight, addr, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(cell.Text)
                    && d.Summary != null
                    && d.Summary.IndexOf(cell.Text, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private IList<DiffItem> FilterDiffsForTable(TableBlock left, TableBlock right)
        {
            string idL = left != null ? left.Id : null;
            string idR = right != null ? right.Id : null;
            var list = new List<DiffItem>();
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

                bool match =
                    (!string.IsNullOrEmpty(idL) && string.Equals(d.TableIdLeft, idL, StringComparison.Ordinal))
                    || (!string.IsNullOrEmpty(idR) && string.Equals(d.TableIdRight, idR, StringComparison.Ordinal));
                if (match || (string.IsNullOrEmpty(d.TableIdLeft) && string.IsNullOrEmpty(d.TableIdRight)))
                {
                    // TableId 未設定の差分も同一シートなら候補（過度に広くしないよう ID 優先）
                    if (match)
                    {
                        list.Add(d);
                    }
                }
            }

            // ID 一致が無い場合はシート上のテーブル差分をすべて渡す（単一表が多い）
            if (list.Count == 0)
            {
                foreach (DiffItem d in _sheetDiffs)
                {
                    if (d != null
                        && (d.Kind == DiffKind.TableRowDelete
                            || d.Kind == DiffKind.TableRowInsert
                            || d.Kind == DiffKind.TableCellChange))
                    {
                        list.Add(d);
                    }
                }
            }

            return list;
        }

        private DiffItem FindImageDiff(EmbeddedImage leftImg, EmbeddedImage rightImg, AlignOp op)
        {
            string leftPath = leftImg != null ? leftImg.ExtractedPath : null;
            string rightPath = rightImg != null ? rightImg.ExtractedPath : null;
            foreach (DiffItem d in _sheetDiffs)
            {
                if (d == null)
                {
                    continue;
                }

                if (d.Kind == DiffKind.Image
                    && PathsEqual(d.LeftImagePath, leftPath)
                    && PathsEqual(d.RightImagePath, rightPath))
                {
                    return d;
                }

                if (d.Kind == DiffKind.ImageOnlyLeft && PathsEqual(d.LeftImagePath, leftPath))
                {
                    return d;
                }

                if (d.Kind == DiffKind.ImageOnlyRight && PathsEqual(d.RightImagePath, rightPath))
                {
                    return d;
                }
            }

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

            // Match で差分なし → パス片側ヒット
            foreach (DiffItem d in _sheetDiffs)
            {
                if (d == null || d.Kind != DiffKind.Image)
                {
                    continue;
                }

                if (PathsEqual(d.LeftImagePath, leftPath) || PathsEqual(d.RightImagePath, rightPath))
                {
                    return d;
                }
            }

            return null;
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static TextBlock CreateHint(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                Margin = new Thickness(8),
                FontSize = 12
            };
        }

        private void StreamScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_suppressScrollEvent)
            {
                return;
            }

            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0)
            {
                return;
            }

            Action<double> handler = VerticalScrollRatioChanged;
            if (handler != null)
            {
                handler(GetVerticalScrollRatio());
            }
        }
    }
}
