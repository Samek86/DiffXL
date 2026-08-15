using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// 次の差分の対象種別。本文ストリームと高さマップは変えない。
    /// </summary>
    public enum StreamKindFilter
    {
        /// <summary>すべての差分。</summary>
        All = 0,

        /// <summary>表（TableHeader / TableRow / Table*）。</summary>
        Table = 1,

        /// <summary>画像ブロック。</summary>
        Image = 2,

        /// <summary>表の外のセル（LooseRow）。</summary>
        Cell = 3
    }

    /// <summary>
    /// 内容ストリームへのスクロール適用モード。
    /// </summary>
    public enum ContentScrollApplyMode
    {
        /// <summary>通常（本文操作・同期）。</summary>
        Normal = 0,

        /// <summary>MiniMap ドラッグ中（位置優先・描画間引き可）。</summary>
        Scrub = 1,

        /// <summary>MiniMap ドラッグ終了（フル描画確定）。</summary>
        ScrubEnd = 2
    }

    /// <summary>
    /// シート内容をドキュメント順の統一ストリームで表示する（セル・表・画像・図形を 1 本に並べる）。
    /// 表は行単位に展開し、ビューポート付近のみ Visual 化する。
    /// 左右は同一 ContentStreamLayout を共有する。
    /// </summary>
    public partial class ContentPane : UserControl
    {
        /// <summary>ビューポート外バッファ行数。</summary>
        private const int RealizeBuffer = 12;

        /// <summary>スクラブ中にフル行生成を許可する index 移動量。</summary>
        private const int ScrubIndexDeltaForFull = 6;

        /// <summary>スクラブ中フル生成のフレーム間隔。</summary>
        private const int ScrubFullRealizeInterval = 3;

        /// <summary>共有レイアウト（ペア＋高さマップ）。</summary>
        private ContentStreamLayout _layout;

        /// <summary>HeightsChanged 購読中のレイアウト。</summary>
        private ContentStreamLayout _layoutSubscribed;

        /// <summary>現在のアライン済みストリーム（左右共通）。</summary>
        private IList<ContentStreamPair> _pairs = new List<ContentStreamPair>();

        /// <summary>実現中 index → ホスト要素。</summary>
        private readonly Dictionary<int, FrameworkElement> _realized =
            new Dictionary<int, FrameworkElement>();

        /// <summary>プレースホルダとして実現中の index。</summary>
        private readonly HashSet<int> _placeholderIndices = new HashSet<int>();

        /// <summary>スクラブ中は高さ実測補正を止める。</summary>
        private bool _suppressHeightCapture;

        /// <summary>スクラブ中のフル Realize カウンタ。</summary>
        private int _scrubFrameCounter;

        /// <summary>前回スクラブ Realize の先頭 index。</summary>
        private int _lastScrubFirst = -1;

        /// <summary>左ペインかどうか。</summary>
        private bool _isLeft = true;

        /// <summary>シート差分。</summary>
        private IList<DiffItem> _sheetDiffs = new List<DiffItem>();

        /// <summary>差分強調（画像枠・セル黄ハイライト）表示。</summary>
        private bool _highlightVisible = true;

        /// <summary>実現中の画像ビュー。</summary>
        private readonly List<ImagePairView> _imagePairViews = new List<ImagePairView>();

        /// <summary>実現中のセル黄ハイライト対象（再構築なしで ON/OFF）。</summary>
        private readonly List<CellHighlightChrome> _cellHighlightChromes = new List<CellHighlightChrome>();

        /// <summary>実現中の TableDiffGrid（互換フォールバック経路）。</summary>
        private readonly List<TableDiffGrid> _tableDiffGrids = new List<TableDiffGrid>();

        /// <summary>
        /// セル／行マーカーの差分色をトグルする対象。
        /// </summary>
        private sealed class CellHighlightChrome
        {
            public Border Target;
            public TextBlock Marker;
            public Brush OnBrush;
            public Brush OffBrush;
            public Brush OnMarkerBrush;
            public Brush OffMarkerBrush;
            public Brush OnBorderBrush;
            public Brush OffBorderBrush;
        }

        /// <summary>スクロール同期中の再入防止。</summary>
        private bool _suppressScrollEvent;

        /// <summary>仮想化更新中（スペーサ変更の ScrollChanged 抑制）。</summary>
        private bool _suppressVirtualize;

        /// <summary>現在選択中のペア index（-1=なし）。</summary>
        private int _selectedPairIndex = -1;

        /// <summary>次の差分の種類フィルタ（本文は隠さない）。</summary>
        private StreamKindFilter _kindFilter = StreamKindFilter.All;

        /// <summary>実現範囲。</summary>
        private int _firstRealized = -1;

        /// <summary>実現範囲。</summary>
        private int _lastRealized = -1;

        public ContentPane()
        {
            InitializeComponent();
            SyncKindFilterChips();
        }

        /// <summary>
        /// 種類フィルタが変わった（左右同期用）。
        /// </summary>
        public event Action<StreamKindFilter> KindFilterChanged;

        /// <summary>
        /// 次の差分の対象種別。Realize / 高さマップは変えない。
        /// </summary>
        public StreamKindFilter KindFilter
        {
            get { return _kindFilter; }
            set
            {
                if (_kindFilter == value)
                {
                    SyncKindFilterChips();
                    return;
                }

                _kindFilter = value;
                SyncKindFilterChips();
                Action<StreamKindFilter> handler = KindFilterChanged;
                if (handler != null)
                {
                    handler(_kindFilter);
                }
            }
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
        /// 共有レイアウト（null 可）。
        /// </summary>
        public ContentStreamLayout Layout
        {
            get { return _layout; }
        }

        /// <summary>
        /// ペア行の高さ（レイアウトマップ）。未確定時は 0。
        /// </summary>
        public double GetPairElementHeight(int index)
        {
            if (_layout == null)
            {
                return 0;
            }

            return _layout.GetHeight(index);
        }

        /// <summary>
        /// 各ペア行に MinHeight を設定する（互換: 共有 height map を更新）。
        /// </summary>
        public void SetPairMinHeights(IList<double> heights)
        {
            if (heights == null || _layout == null)
            {
                return;
            }

            bool any = false;
            int n = Math.Min(_layout.Count, heights.Count);
            for (int i = 0; i < n; i++)
            {
                if (_layout.TryUpdateHeight(i, heights[i]))
                {
                    any = true;
                }
            }

            if (any)
            {
                RealizeViewport(force: true);
            }
        }

        /// <summary>
        /// 左右の行高は共有 ContentStreamLayout を強制適用する（テーブル行ギャップずれ防止）。
        /// </summary>
        public static void SyncPairHeights(ContentPane left, ContentPane right)
        {
            if (left != null)
            {
                left.ApplyRealizedHeightsFromMap();
            }

            if (right != null)
            {
                right.ApplyRealizedHeightsFromMap();
            }
        }

        /// <summary>
        /// レイアウト購読を切り替える。
        /// </summary>
        private void AttachLayout(ContentStreamLayout layout)
        {
            if (_layoutSubscribed != null)
            {
                _layoutSubscribed.HeightsChanged -= OnLayoutHeightsChanged;
                _layoutSubscribed = null;
            }

            _layout = layout;
            if (layout != null)
            {
                layout.HeightsChanged += OnLayoutHeightsChanged;
                _layoutSubscribed = layout;
            }
        }

        private void OnLayoutHeightsChanged()
        {
            ApplyRealizedHeightsFromMap();
            UpdateSpacersForRealizedRange();
        }

        /// <summary>
        /// 実現中要素に共有 height map を強制適用する。
        /// テーブル行は左右とも同じ Height に固定し、「この側になし」のずれを防ぐ。
        /// </summary>
        private void ApplyRealizedHeightsFromMap()
        {
            if (_layout == null || _realized.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<int, FrameworkElement> kv in _realized)
            {
                ApplyForcedHeight(kv.Value, kv.Key);
            }
        }

        private void ApplyForcedHeight(FrameworkElement wrap, int index)
        {
            if (wrap == null || _layout == null)
            {
                return;
            }

            double h = _layout.GetHeight(index);
            if (h < 8)
            {
                return;
            }

            // テーブル行は margin 2、その他は 10
            double margin = _layout.IsUniformHeightPair(index) ? 2.0 : 10.0;
            double inner = Math.Max(8, h - margin);
            wrap.MinHeight = inner;

            if (_layout.IsUniformHeightPair(index))
            {
                wrap.Height = inner;
                wrap.MaxHeight = inner;
            }
            else
            {
                wrap.ClearValue(HeightProperty);
                wrap.ClearValue(MaxHeightProperty);
            }
        }

        private void UpdateSpacersForRealizedRange()
        {
            if (_layout == null || _firstRealized < 0 || _lastRealized < _firstRealized)
            {
                return;
            }

            _suppressVirtualize = true;
            _suppressScrollEvent = true;
            try
            {
                if (TopSpacer != null)
                {
                    TopSpacer.Height = _layout.OffsetOf(_firstRealized);
                }

                if (BottomSpacer != null)
                {
                    double afterLast = _layout.OffsetOf(_lastRealized + 1);
                    BottomSpacer.Height = Math.Max(0, _layout.TotalHeight - afterLast);
                }
            }
            finally
            {
                _suppressScrollEvent = false;
                _suppressVirtualize = false;
            }
        }

        /// <summary>
        /// 表示中シート名。
        /// </summary>
        public string SheetName { get; private set; }

        /// <summary>
        /// 差分強調（画像枠・セル黄ハイライト）の表示を伝播する。再比較・再構築なし。
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

            ApplyCellHighlightChrome(visible);

            foreach (TableDiffGrid grid in _tableDiffGrids)
            {
                if (grid != null)
                {
                    grid.SetHighlightVisible(visible);
                }
            }
        }

        /// <summary>
        /// 登録済みセル黄ハイライトの背景を切り替える。
        /// </summary>
        private void ApplyCellHighlightChrome(bool visible)
        {
            for (int i = 0; i < _cellHighlightChromes.Count; i++)
            {
                CellHighlightChrome chrome = _cellHighlightChromes[i];
                if (chrome == null)
                {
                    continue;
                }

                if (chrome.Target != null)
                {
                    if (chrome.OnBrush != null && chrome.OffBrush != null)
                    {
                        chrome.Target.Background = visible ? chrome.OnBrush : chrome.OffBrush;
                    }

                    if (chrome.OnBorderBrush != null && chrome.OffBorderBrush != null)
                    {
                        chrome.Target.BorderBrush = visible ? chrome.OnBorderBrush : chrome.OffBorderBrush;
                    }
                }

                if (chrome.Marker != null && chrome.OnMarkerBrush != null && chrome.OffMarkerBrush != null)
                {
                    chrome.Marker.Foreground = visible ? chrome.OnMarkerBrush : chrome.OffMarkerBrush;
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
        /// 左右は絶対の left/right としてアラインし、共有レイアウトを取得する。
        /// </summary>
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

            AttachLayout(ContentStreamBuilder.GetOrBuildLayout(leftSheet, rightSheet));
            _pairs = _layout != null ? _layout.Pairs : new List<ContentStreamPair>();

            string side = isLeft ? "左" : "右";
            int selfBlocks = 0;
            if (_pairs != null)
            {
                for (int i = 0; i < _pairs.Count; i++)
                {
                    ContentStreamPair p = _pairs[i];
                    ContentStreamBlock b = isLeft
                        ? (p != null ? p.Left : null)
                        : (p != null ? p.Right : null);
                    if (b != null)
                    {
                        selfBlocks++;
                    }
                }
            }

            int diffCount = _sheetDiffs.Count;
            HeaderText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} · シート「{1}」 · 表示行 {2} · 対応行 {3} · 差分 {4} 件（仮想化・統一表示）",
                side,
                SheetName ?? "（なし）",
                selfBlocks,
                _pairs != null ? _pairs.Count : 0,
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
                // レイアウトマップから推定
                if (_layout != null && _layout.TotalHeight > 1
                    && StreamScroll.ViewportHeight > 1)
                {
                    double scrollable = Math.Max(0, _layout.TotalHeight - StreamScroll.ViewportHeight);
                    if (scrollable <= 0.5)
                    {
                        return 0;
                    }

                    return Math.Max(0, Math.Min(1, StreamScroll.VerticalOffset / scrollable));
                }

                return 0;
            }

            return Math.Max(0, Math.Min(1, StreamScroll.VerticalOffset / extent));
        }

        /// <summary>
        /// ビューポートが高さ全体に占める割合 0..1（スクロール不能なら 1）。
        /// </summary>
        public double GetVisibleFraction()
        {
            if (StreamScroll == null)
            {
                return 1;
            }

            double viewport = StreamScroll.ViewportHeight;
            double extent = StreamScroll.ExtentHeight;
            if (extent <= 0.5 && _layout != null && _layout.TotalHeight > 1)
            {
                extent = _layout.TotalHeight;
            }

            return MiniMapViewportBand.VisibleFraction(viewport, extent);
        }

        /// <summary>
        /// 縦スクロール比率 0..1 を設定する（同期用。イベントは出さない）。
        /// </summary>
        public void SetVerticalScrollRatio(double ratio)
        {
            SetVerticalScrollRatio(ratio, ContentScrollApplyMode.Normal);
        }

        /// <summary>
        /// 縦スクロール比率 0..1 を設定する（MiniMap スクラブモード対応）。
        /// </summary>
        public void SetVerticalScrollRatio(double ratio, ContentScrollApplyMode mode)
        {
            if (StreamScroll == null)
            {
                return;
            }

            ratio = Math.Max(0, Math.Min(1, ratio));
            double target;
            double extent = StreamScroll.ScrollableHeight;
            if (extent > 0.5)
            {
                target = ratio * extent;
            }
            else if (_layout != null && _layout.TotalHeight > 1)
            {
                double vp = Math.Max(1, StreamScroll.ViewportHeight);
                double scrollable = Math.Max(0, _layout.TotalHeight - vp);
                target = ratio * scrollable;
            }
            else
            {
                target = 0;
            }

            bool offsetUnchanged = Math.Abs(StreamScroll.VerticalOffset - target) < 0.5;
            if (!offsetUnchanged)
            {
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

            if (mode == ContentScrollApplyMode.Scrub)
            {
                _suppressHeightCapture = true;
                _scrubFrameCounter++;
                RealizeViewport(force: false, scrub: true);
                return;
            }

            if (mode == ContentScrollApplyMode.ScrubEnd)
            {
                _suppressHeightCapture = false;
                _scrubFrameCounter = 0;
                _lastScrubFirst = -1;
                RealizeViewport(force: true, scrub: false, replacePlaceholders: true);
                return;
            }

            // Normal
            _suppressHeightCapture = false;
            if (!offsetUnchanged || _placeholderIndices.Count > 0)
            {
                RealizeViewport(
                    force: _placeholderIndices.Count > 0,
                    scrub: false,
                    replacePlaceholders: _placeholderIndices.Count > 0);
            }
            else
            {
                RealizeViewport(force: false, scrub: false);
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
        /// 選択中のストリーム index（-1 は未選択）。
        /// </summary>
        public int SelectedPairIndex
        {
            get { return _selectedPairIndex; }
        }

        /// <summary>
        /// ペア index を表示領域へスクロールし、選択枠で強調する。
        /// 要素未生成でも height map オフセットでジャンプできる。
        /// </summary>
        public bool ScrollToPairIndex(int index)
        {
            if (_layout == null || index < 0 || index >= _layout.Count || StreamScroll == null)
            {
                return false;
            }

            double target = Math.Max(0, _layout.OffsetOf(index) - 8);
            _suppressScrollEvent = true;
            try
            {
                StreamScroll.ScrollToVerticalOffset(target);
            }
            finally
            {
                _suppressScrollEvent = false;
            }

            ApplyPairSelection(index);
            RealizeViewport(force: true);
            return true;
        }

        /// <summary>
        /// スクロールせず選択枠だけ付ける。
        /// </summary>
        public void HighlightPairIndex(int index)
        {
            ApplyPairSelection(index);
        }

        /// <summary>
        /// 現在シートの差分ペア index（昇順）。Skip 行 ∪ 非 Structure の StreamPairIndex。
        /// </summary>
        public IList<int> GetDiffPairIndices()
        {
            return GetDiffPairIndices(_sheetDiffs);
        }

        /// <summary>
        /// 差分ペア index（昇順）。Skip 行 ∪ 指定アイテムの StreamPairIndex。
        /// </summary>
        public IList<int> GetDiffPairIndices(IEnumerable<DiffItem> items)
        {
            return DiffPairNavigator.CollectDiffPairIndices(_pairs, items, _kindFilter);
        }

        /// <summary>
        /// VerticalOffset が指すペア index（ScrollToPairIndex の -8px 余白を補正）。未配置は -1。
        /// </summary>
        public int GetPairIndexAtVerticalOffset()
        {
            if (_layout == null || _layout.Count == 0)
            {
                return -1;
            }

            if (StreamScroll == null)
            {
                return _selectedPairIndex;
            }

            return _layout.IndexAtOffset(StreamScroll.VerticalOffset + 8);
        }

        /// <summary>
        /// 選択枠を付け替える（実現中の要素のみ）。
        /// </summary>
        private void ApplyPairSelection(int index)
        {
            _selectedPairIndex = index;
            foreach (KeyValuePair<int, FrameworkElement> kv in _realized)
            {
                var border = kv.Value as Border;
                if (border == null)
                {
                    continue;
                }

                bool selected = kv.Key == index;
                ApplyBorderChrome(border, selected, IsGapBorder(border));
            }
        }

        private static bool IsGapBorder(Border border)
        {
            if (border == null)
            {
                return false;
            }

            var scb = border.Background as SolidColorBrush;
            if (scb == null)
            {
                return false;
            }

            Color c = scb.Color;
            return c.R == 0xF3 && c.G == 0xF4 && c.B == 0xF6;
        }

        private static void ApplyBorderChrome(Border border, bool selected, bool isGap)
        {
            border.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))
                : (isGap
                    ? new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB))
                    : new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)));
            border.BorderThickness = new Thickness(selected ? 3 : 1);
            if (selected)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
            }
            else if (!isGap)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            }
            else
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            }
        }

        /// <summary>
        /// ストリーム比率 0..1 の位置へスクロール。
        /// </summary>
        public void ScrollToStreamRatio(double ratio)
        {
            SetVerticalScrollRatio(ratio);
        }

        /// <summary>
        /// DiffItem に対応するストリーム行へジャンプする。
        /// </summary>
        public bool ScrollToDiffItem(DiffItem item)
        {
            if (item == null)
            {
                return false;
            }

            int index = FindPairIndexForDiffItem(item);
            if (index >= 0)
            {
                return ScrollToPairIndex(index);
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
        /// DiffItem から統一ストリームのペア index を解決する。
        /// </summary>
        public int FindPairIndexForDiffItem(DiffItem item)
        {
            if (item == null || _pairs == null || _pairs.Count == 0)
            {
                return -1;
            }

            if (item.StreamPairIndex >= 0 && item.StreamPairIndex < _pairs.Count)
            {
                return item.StreamPairIndex;
            }

            if (item.Kind == DiffKind.Image
                || item.Kind == DiffKind.ImageOnlyLeft
                || item.Kind == DiffKind.ImageOnlyRight)
            {
                for (int i = 0; i < _pairs.Count; i++)
                {
                    ContentStreamPair p = _pairs[i];
                    if (p == null)
                    {
                        continue;
                    }

                    if (BlockMatchesImage(p.Left, item) || BlockMatchesImage(p.Right, item))
                    {
                        return i;
                    }
                }
            }

            if (item.Kind == DiffKind.TableRowDelete
                || item.Kind == DiffKind.TableRowInsert
                || item.Kind == DiffKind.TableCellChange)
            {
                int targetRow = TextDiffService.ParseAnchorRow(item.AddressLeft);
                if (targetRow <= 0)
                {
                    targetRow = TextDiffService.ParseAnchorRow(item.AddressRight);
                }

                int firstTableHit = -1;
                for (int i = 0; i < _pairs.Count; i++)
                {
                    ContentStreamPair p = _pairs[i];
                    if (p == null)
                    {
                        continue;
                    }

                    if (!BlockMatchesTable(p.Left, item) && !BlockMatchesTable(p.Right, item))
                    {
                        continue;
                    }

                    if (firstTableHit < 0)
                    {
                        firstTableHit = i;
                    }

                    // 行指定があれば TableRow の Excel 行で絞る
                    if (targetRow > 0)
                    {
                        if ((p.Left != null && p.Left.Kind == ContentBlockKind.TableRow && p.Left.Row == targetRow)
                            || (p.Right != null && p.Right.Kind == ContentBlockKind.TableRow && p.Right.Row == targetRow))
                        {
                            return i;
                        }
                    }
                }

                if (firstTableHit >= 0)
                {
                    return firstTableHit;
                }
            }

            int row = TextDiffService.ParseAnchorRow(item.AddressLeft);
            if (row <= 0)
            {
                row = TextDiffService.ParseAnchorRow(item.AddressRight);
            }

            if (row > 0)
            {
                for (int i = 0; i < _pairs.Count; i++)
                {
                    ContentStreamPair p = _pairs[i];
                    if (p == null)
                    {
                        continue;
                    }

                    if ((p.Left != null && p.Left.Row == row)
                        || (p.Right != null && p.Right.Row == row))
                    {
                        return i;
                    }
                }
            }

            if (item.OrderHint > 0)
            {
                return ContentStreamBuilder.FindNearestPairIndex(_pairs, item.OrderHint);
            }

            return -1;
        }

        private static bool BlockMatchesImage(ContentStreamBlock block, DiffItem item)
        {
            if (block == null || block.Kind != ContentBlockKind.Image || block.Image == null || item == null)
            {
                return false;
            }

            string path = block.Image.ExtractedPath;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return string.Equals(path, item.LeftImagePath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, item.RightImagePath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool BlockMatchesTable(ContentStreamBlock block, DiffItem item)
        {
            if (block == null || block.Table == null || item == null)
            {
                return false;
            }

            if (block.Kind != ContentBlockKind.Table
                && block.Kind != ContentBlockKind.TableHeader
                && block.Kind != ContentBlockKind.TableRow)
            {
                return false;
            }

            string id = block.Table.Id;
            if (string.IsNullOrEmpty(id))
            {
                return true;
            }

            return string.Equals(id, item.TableIdLeft, StringComparison.Ordinal)
                || string.Equals(id, item.TableIdRight, StringComparison.Ordinal);
        }

        /// <summary>
        /// ストリーム状態をリセットし、ビューポートを実現する。
        /// </summary>
        private void RebuildStream()
        {
            ClearRealized();
            if (TopSpacer != null)
            {
                TopSpacer.Height = 0;
            }

            if (BottomSpacer != null)
            {
                BottomSpacer.Height = 0;
            }

            if (_pairs == null || _pairs.Count == 0)
            {
                if (ViewportHost != null)
                {
                    ViewportHost.Children.Clear();
                    ViewportHost.Children.Add(CreateHint("（表示する内容がありません）"));
                }

                return;
            }

            // 初回は先頭から実現
            if (StreamScroll != null)
            {
                _suppressScrollEvent = true;
                try
                {
                    StreamScroll.ScrollToVerticalOffset(0);
                }
                finally
                {
                    _suppressScrollEvent = false;
                }
            }

            var swRealize = System.Diagnostics.Stopwatch.StartNew();
            RealizeViewport(force: true);
            swRealize.Stop();
            Log.Info("表示Realize=" + swRealize.ElapsedMilliseconds + "ms sheet=" + (SheetName ?? ""));
        }

        private void ClearRealized()
        {
            _realized.Clear();
            _placeholderIndices.Clear();
            _imagePairViews.Clear();
            _cellHighlightChromes.Clear();
            _tableDiffGrids.Clear();
            _firstRealized = -1;
            _lastRealized = -1;
            _lastScrubFirst = -1;
            _scrubFrameCounter = 0;
            if (ViewportHost != null)
            {
                ViewportHost.Children.Clear();
            }
        }

        /// <summary>
        /// 可視範囲±バッファの要素だけ生成する。
        /// </summary>
        private void RealizeViewport(bool force)
        {
            RealizeViewport(force, scrub: false, replacePlaceholders: false);
        }

        /// <summary>
        /// 可視範囲±バッファの要素だけ生成する（スクラブ／プレースホルダ対応）。
        /// </summary>
        private void RealizeViewport(bool force, bool scrub, bool replacePlaceholders = false)
        {
            if (ViewportHost == null || StreamScroll == null || _layout == null || _layout.Count == 0)
            {
                return;
            }

            double offset = StreamScroll.VerticalOffset;
            double viewport = StreamScroll.ViewportHeight;
            if (viewport < 1)
            {
                viewport = Math.Max(ActualHeight - 40, 200);
            }

            int first = Math.Max(0, _layout.IndexAtOffset(offset) - RealizeBuffer);
            int last = Math.Min(
                _layout.Count - 1,
                _layout.IndexAtOffset(offset + viewport) + RealizeBuffer);

            bool rangeSame = first == _firstRealized && last == _lastRealized;
            if (!force && !scrub && !replacePlaceholders && rangeSame && _placeholderIndices.Count == 0)
            {
                return;
            }

            // スクラブ: 範囲が同じでプレースホルダ置換も不要なら何もしない
            if (scrub && !force && rangeSame && !replacePlaceholders)
            {
                return;
            }

            bool allowFullCreate = !scrub || ShouldFullCreateDuringScrub(first);
            if (replacePlaceholders)
            {
                allowFullCreate = true;
            }

            _suppressVirtualize = true;
            try
            {
                // 範囲外を破棄
                var remove = new List<int>();
                foreach (int key in _realized.Keys)
                {
                    if (key < first || key > last)
                    {
                        remove.Add(key);
                    }
                }

                for (int r = 0; r < remove.Count; r++)
                {
                    int idx = remove[r];
                    FrameworkElement el;
                    if (_realized.TryGetValue(idx, out el))
                    {
                        DetachImageViews(el);
                        _realized.Remove(idx);
                    }

                    _placeholderIndices.Remove(idx);
                }

                // 不足分・プレースホルダ置換
                for (int i = first; i <= last; i++)
                {
                    bool isPh = _placeholderIndices.Contains(i);
                    if (!_realized.ContainsKey(i))
                    {
                        if (allowFullCreate)
                        {
                            _realized[i] = CreatePairElement(i);
                            _placeholderIndices.Remove(i);
                        }
                        else
                        {
                            _realized[i] = CreatePlaceholderElement(i);
                            _placeholderIndices.Add(i);
                        }
                    }
                    else if (isPh && allowFullCreate)
                    {
                        DetachImageViews(_realized[i]);
                        _realized[i] = CreatePairElement(i);
                        _placeholderIndices.Remove(i);
                    }
                    else if (replacePlaceholders && isPh)
                    {
                        DetachImageViews(_realized[i]);
                        _realized[i] = CreatePairElement(i);
                        _placeholderIndices.Remove(i);
                    }
                }

                // 表示順に積み直し
                ViewportHost.Children.Clear();
                for (int i = first; i <= last; i++)
                {
                    FrameworkElement el;
                    if (_realized.TryGetValue(i, out el) && el != null)
                    {
                        ViewportHost.Children.Add(el);
                    }
                }

                double top = _layout.OffsetOf(first);
                double afterLast = _layout.OffsetOf(last + 1);
                double bottom = Math.Max(0, _layout.TotalHeight - afterLast);
                if (TopSpacer != null)
                {
                    TopSpacer.Height = top;
                }

                if (BottomSpacer != null)
                {
                    BottomSpacer.Height = bottom;
                }

                _firstRealized = first;
                _lastRealized = last;
                if (scrub)
                {
                    _lastScrubFirst = first;
                }

                // 選択枠再適用（プレースホルダ以外）
                if (_selectedPairIndex >= 0)
                {
                    ApplyPairSelection(_selectedPairIndex);
                }
            }
            finally
            {
                _suppressVirtualize = false;
            }

            // 実測高さでマップ補正（スクラブ中は抑制）
            if (!_suppressHeightCapture)
            {
                Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(CaptureMeasuredHeights));
            }
        }

        /// <summary>
        /// スクラブ中にフル行生成してよいか。
        /// </summary>
        private bool ShouldFullCreateDuringScrub(int first)
        {
            if (_lastScrubFirst < 0)
            {
                return true;
            }

            if (Math.Abs(first - _lastScrubFirst) >= ScrubIndexDeltaForFull)
            {
                return true;
            }

            return (_scrubFrameCounter % ScrubFullRealizeInterval) == 0;
        }

        /// <summary>
        /// 高速スクラブ用の固定高プレースホルダ。
        /// </summary>
        private FrameworkElement CreatePlaceholderElement(int index)
        {
            double h = _layout != null ? _layout.GetHeight(index) : 40;
            if (h < 12)
            {
                h = 32;
            }

            return new Border
            {
                Tag = index,
                Height = Math.Max(8, h - 10),
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
                Child = new TextBlock
                {
                    Text = "…",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 4, 8, 4)
                }
            };
        }

        private void CaptureMeasuredHeights()
        {
            if (_suppressHeightCapture)
            {
                return;
            }

            if (_layout == null || _realized.Count == 0)
            {
                return;
            }

            // プレースホルダは実測しない
            if (_placeholderIndices.Count > 0 && _placeholderIndices.Count == _realized.Count)
            {
                return;
            }

            bool changed = false;
            foreach (KeyValuePair<int, FrameworkElement> kv in _realized)
            {
                if (_placeholderIndices.Contains(kv.Key))
                {
                    continue;
                }

                FrameworkElement el = kv.Value;
                if (el == null)
                {
                    continue;
                }

                double h = el.ActualHeight;
                if (h < 1)
                {
                    h = el.DesiredSize.Height;
                }

                if (h > 1)
                {
                    double margin = _layout.IsUniformHeightPair(kv.Key) ? 2.0 : 10.0;
                    h += margin;
                    if (_layout.TryUpdateHeight(kv.Key, h))
                    {
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                ApplyRealizedHeightsFromMap();
                UpdateSpacersForRealizedRange();
            }
            else
            {
                // マップは変わらなくても、初回レイアウト後に強制高を当てる
                ApplyRealizedHeightsFromMap();
            }
        }

        private void DetachImageViews(FrameworkElement el)
        {
            if (el == null)
            {
                return;
            }

            for (int i = _imagePairViews.Count - 1; i >= 0; i--)
            {
                ImagePairView v = _imagePairViews[i];
                if (v != null && IsDescendant(el, v))
                {
                    _imagePairViews.RemoveAt(i);
                }
            }

            for (int i = _cellHighlightChromes.Count - 1; i >= 0; i--)
            {
                CellHighlightChrome c = _cellHighlightChromes[i];
                if (c == null)
                {
                    _cellHighlightChromes.RemoveAt(i);
                    continue;
                }

                if ((c.Target != null && IsDescendant(el, c.Target))
                    || (c.Marker != null && IsDescendant(el, c.Marker)))
                {
                    _cellHighlightChromes.RemoveAt(i);
                }
            }

            for (int i = _tableDiffGrids.Count - 1; i >= 0; i--)
            {
                TableDiffGrid g = _tableDiffGrids[i];
                if (g != null && IsDescendant(el, g))
                {
                    _tableDiffGrids.RemoveAt(i);
                }
            }
        }

        private static bool IsDescendant(DependencyObject root, DependencyObject node)
        {
            DependencyObject cur = node;
            while (cur != null)
            {
                if (ReferenceEquals(cur, root))
                {
                    return true;
                }

                cur = VisualTreeHelper.GetParent(cur);
            }

            return false;
        }

        private FrameworkElement CreatePairElement(int index)
        {
            ContentStreamPair pair = _pairs[index];
            ContentStreamBlock self = _isLeft ? pair.Left : pair.Right;
            ContentStreamBlock partner = _isLeft ? pair.Right : pair.Left;
            bool isGap = self == null;

            FrameworkElement blockUi;
            bool tableRowPair = (self != null && self.Kind == ContentBlockKind.TableRow)
                || (partner != null && partner.Kind == ContentBlockKind.TableRow)
                || (self != null && self.Kind == ContentBlockKind.TableHeader)
                || (partner != null && partner.Kind == ContentBlockKind.TableHeader);

            if (isGap && partner != null && partner.Kind == ContentBlockKind.TableRow)
            {
                // テーブル行ギャップは「1 行分」の表 UI（大きな説明ブロックにしない）
                blockUi = (FrameworkElement)CreateTableRowUi(pair, self: null, partner);
            }
            else if (isGap)
            {
                blockUi = CreateGapBlock(pair, partner);
            }
            else if (self.Kind == ContentBlockKind.TableRow)
            {
                // 余白を増やさず 1 行高に揃える
                blockUi = (FrameworkElement)CreateTableRowUi(pair, self, partner);
            }
            else if (self.Kind == ContentBlockKind.TableHeader)
            {
                blockUi = (FrameworkElement)CreateTableHeaderUi(self, partner, pair.Op);
            }
            else
            {
                blockUi = CreateBlockUi(pair, self, partner);
            }

            var wrap = new Border
            {
                Tag = index,
                // テーブル行は行間を詰めて左右同じピッチにする
                Margin = tableRowPair
                    ? new Thickness(0, 0, 0, 2)
                    : new Thickness(0, 0, 0, 10),
                BorderBrush = isGap
                    ? new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB))
                    : new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(tableRowPair ? 1 : 1),
                CornerRadius = new CornerRadius(tableRowPair ? 3 : 6),
                Background = isGap
                    ? new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                Child = blockUi,
                Padding = tableRowPair ? new Thickness(2, 1, 2, 1) : new Thickness(0)
            };

            ApplyForcedHeight(wrap, index);

            if (index == _selectedPairIndex)
            {
                ApplyBorderChrome(wrap, selected: true, isGap: isGap);
            }

            return wrap;
        }

        private FrameworkElement CreateBlockUi(
            ContentStreamPair pair,
            ContentStreamBlock self,
            ContentStreamBlock partner)
        {
            var panel = new StackPanel { Margin = new Thickness(8) };

            switch (self.Kind)
            {
                case ContentBlockKind.TableHeader:
                    panel.Children.Add(CreateTableHeaderUi(self, partner, pair.Op));
                    break;
                case ContentBlockKind.TableRow:
                    panel.Children.Add(CreateTableRowUi(pair, self, partner));
                    break;
                case ContentBlockKind.Table:
                    // 未展開フォールバック（互換）
                    panel.Children.Add(CreateKindHeader(self, pair.Op));
                    {
                        TableBlock leftT;
                        TableBlock rightT;
                        if (_isLeft)
                        {
                            leftT = self.Table;
                            rightT = partner != null ? partner.Table : null;
                        }
                        else
                        {
                            leftT = partner != null ? partner.Table : null;
                            rightT = self.Table;
                        }

                        IList<DiffItem> tableDiffs = FilterDiffsForTable(leftT, rightT);
                        var grid = new TableDiffGrid();
                        grid.Load(leftT, rightT, tableDiffs, _isLeft, _highlightVisible);
                        _tableDiffGrids.Add(grid);
                        panel.Children.Add(grid);
                    }
                    break;
                case ContentBlockKind.LooseRow:
                    panel.Children.Add(CreateKindHeader(self, pair.Op));
                    panel.Children.Add(CreateLooseRowUi(self));
                    break;
                case ContentBlockKind.Image:
                    panel.Children.Add(CreateKindHeader(self, pair.Op));
                    {
                        EmbeddedImage leftImg;
                        EmbeddedImage rightImg;
                        if (_isLeft)
                        {
                            leftImg = self.Image;
                            rightImg = partner != null ? partner.Image : null;
                        }
                        else
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
                    panel.Children.Add(CreateKindHeader(self, pair.Op));
                    panel.Children.Add(CreateShapeUi(self));
                    break;
                default:
                    panel.Children.Add(CreateKindHeader(self, pair.Op));
                    break;
            }

            return panel;
        }

        private UIElement CreateTableHeaderUi(
            ContentStreamBlock self,
            ContentStreamBlock partner,
            AlignOp op)
        {
            TableBlock selfT = self != null ? self.Table : null;
            TableBlock partnerT = partner != null ? partner.Table : null;
            string side = _isLeft ? "左" : "右";
            string id = selfT != null ? (selfT.Id ?? "?") : "—";
            string partnerId = partnerT != null ? (partnerT.Id ?? "?") : "—";
            string range = selfT != null
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "R{0}-{1} C{2}-{3}",
                    selfT.RowStart,
                    selfT.RowEnd,
                    selfT.ColStart,
                    selfT.ColEnd)
                : "（なし）";
            string mark = op == AlignOp.Match ? "＝" : "±";
            var panel = new StackPanel();
            if (selfT != null)
            {
                panel.ToolTip = DetectionSourceTooltip(selfT.DetectionSource);
            }

            panel.Children.Add(new TextBlock
            {
                Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} テーブル [{1}] · {2}",
                    mark,
                    id,
                    range),
                Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} · 対応 {1} ↔ {2}",
                    side,
                    _isLeft ? id : partnerId,
                    _isLeft ? partnerId : id),
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            });
            return panel;
        }

        /// <summary>
        /// 表ヘッダーの検出元ツールチップ。
        /// </summary>
        private static string DetectionSourceTooltip(string source)
        {
            return string.Equals(source, TableDetector.SourceExcelTable, StringComparison.Ordinal)
                ? "検出: Excel 表"
                : "検出: 罫線";
        }

        private UIElement CreateTableRowUi(
            ContentStreamPair pair,
            ContentStreamBlock self,
            ContentStreamBlock partner)
        {
            IList<CellContent> selfCells = self != null ? self.Cells : null;
            IList<CellContent> leftCells = _isLeft ? selfCells : (partner != null ? partner.Cells : null);
            IList<CellContent> rightCells = _isLeft ? (partner != null ? partner.Cells : null) : selfCells;

            string marker;
            Brush markerFg;
            Brush rowBg;
            Brush rowBorder;
            bool isGap = selfCells == null;

            if (pair.Op == AlignOp.SkipLeft)
            {
                if (_isLeft)
                {
                    marker = "− 削除";
                    markerFg = Frozen(0xFF, 0xB9, 0x1C, 0x1C);
                    rowBg = Frozen(0xFF, 0xFE, 0xE2, 0xE2);
                    rowBorder = Frozen(0xFF, 0xF8, 0x71, 0x71);
                }
                else
                {
                    marker = "∅ この側になし";
                    markerFg = Frozen(0xFF, 0x6B, 0x72, 0x80);
                    rowBg = Frozen(0xFF, 0xFE, 0xF2, 0xF2);
                    rowBorder = Frozen(0xFF, 0xF8, 0x71, 0x71);
                    isGap = true;
                }
            }
            else if (pair.Op == AlignOp.SkipRight)
            {
                if (!_isLeft)
                {
                    marker = "+ 追加";
                    markerFg = Frozen(0xFF, 0x04, 0x78, 0x57);
                    rowBg = Frozen(0xFF, 0xD1, 0xFA, 0xE5);
                    rowBorder = Frozen(0xFF, 0x34, 0xD3, 0x99);
                }
                else
                {
                    marker = "∅ この側になし";
                    markerFg = Frozen(0xFF, 0x6B, 0x72, 0x80);
                    rowBg = Frozen(0xFF, 0xEC, 0xFD, 0xF5);
                    rowBorder = Frozen(0xFF, 0x34, 0xD3, 0x99);
                    isGap = true;
                }
            }
            else
            {
                // Match またはその他 → 下記 Match ブロックで上書き
                marker = "＝";
                markerFg = Frozen(0xFF, 0x6B, 0x72, 0x80);
                rowBg = Frozen(0xFF, 0xFF, 0xFF, 0xFF);
                rowBorder = Frozen(0xFF, 0xE5, 0xE7, 0xEB);
            }

            bool matchCellChange = false;
            Brush markerOn = null;
            Brush markerOff = Frozen(0xFF, 0x6B, 0x72, 0x80);
            Brush rowBorderOn = null;
            Brush rowBorderOff = Frozen(0xFF, 0xE5, 0xE7, 0xEB);
            if (pair.Op == AlignOp.Match)
            {
                matchCellChange = HasRowCellChange(leftCells, rightCells);
                marker = matchCellChange ? "± 変更" : "＝";
                markerOn = Frozen(0xFF, 0xCA, 0x8A, 0x04);
                markerFg = matchCellChange && _highlightVisible ? markerOn : markerOff;
                rowBg = Frozen(0xFF, 0xFF, 0xFF, 0xFF);
                rowBorderOn = Frozen(0xFF, 0xCA, 0x8A, 0x04);
                rowBorder = matchCellChange && _highlightVisible ? rowBorderOn : rowBorderOff;
            }

            int colCount = Math.Max(CountCells(leftCells), CountCells(rightCells));
            if (colCount < 1)
            {
                colCount = 1;
            }

            var rowPanel = new DockPanel();
            var markerBlock = new TextBlock
            {
                Text = marker,
                Foreground = markerFg,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                MinWidth = 56,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(markerBlock, Dock.Left);
            rowPanel.Children.Add(markerBlock);

            var cellsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            Brush changeBrush = CreateCellChangeBrush();
            Brush normalCellBrush = Frozen(0xFF, 0xF9, 0xFA, 0xFB);

            for (int c = 0; c < colCount; c++)
            {
                CellContent sc = GetCell(selfCells, c);
                CellContent lc = GetCell(leftCells, c);
                CellContent rc = GetCell(rightCells, c);
                bool changed = pair.Op == AlignOp.Match && IsCellChanged(lc, rc);
                string text;
                Brush cellBg;
                Brush textFg = Frozen(0xFF, 0x11, 0x18, 0x27);

                if (isGap || selfCells == null)
                {
                    text = "·";
                    cellBg = Frozen(0xFF, 0xF3, 0xF4, 0xF6);
                    textFg = Frozen(0xFF, 0x6B, 0x72, 0x80);
                }
                else
                {
                    text = sc != null && sc.Text != null ? sc.Text : " ";
                    if (string.IsNullOrEmpty(text))
                    {
                        text = " ";
                    }

                    cellBg = changed && _highlightVisible ? changeBrush : normalCellBrush;
                }

                var cellBorder = new Border
                {
                    MinWidth = 48,
                    Margin = new Thickness(0, 0, 3, 0),
                    Padding = new Thickness(6, 3, 6, 3),
                    Background = cellBg,
                    BorderBrush = Frozen(0xFF, 0xE5, 0xE7, 0xEB),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Child = new TextBlock
                    {
                        Text = text,
                        Foreground = textFg,
                        FontFamily = new FontFamily("Consolas, Yu Gothic UI, sans-serif"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.NoWrap
                    }
                };
                if (changed && !isGap)
                {
                    _cellHighlightChromes.Add(new CellHighlightChrome
                    {
                        Target = cellBorder,
                        OnBrush = changeBrush,
                        OffBrush = normalCellBrush
                    });
                }

                cellsPanel.Children.Add(cellBorder);
            }

            rowPanel.Children.Add(cellsPanel);

            var outer = new Border
            {
                Padding = new Thickness(4, 3, 4, 3),
                Background = rowBg,
                BorderBrush = rowBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = rowPanel
            };

            if (matchCellChange)
            {
                _cellHighlightChromes.Add(new CellHighlightChrome
                {
                    Target = outer,
                    OnBorderBrush = rowBorderOn,
                    OffBorderBrush = rowBorderOff,
                    Marker = markerBlock,
                    OnMarkerBrush = markerOn,
                    OffMarkerBrush = markerOff
                });
            }

            return outer;
        }

        /// <summary>
        /// 設定の差分色から変更セル用ブラシを作る。
        /// </summary>
        private static Brush CreateCellChangeBrush()
        {
            try
            {
                DiffHighlightStyle style = DiffHighlightStyle.FromSettings();
                Color c = style.ToWpfColor();
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
                return Frozen(0x80, 0xFF, 0xFF, 0x00);
            }
        }

        private static int CountCells(IList<CellContent> row)
        {
            return row != null ? row.Count : 0;
        }

        private static CellContent GetCell(IList<CellContent> row, int index)
        {
            if (row == null || index < 0 || index >= row.Count)
            {
                return null;
            }

            return row[index];
        }

        private static bool HasRowCellChange(IList<CellContent> left, IList<CellContent> right)
        {
            int n = Math.Max(CountCells(left), CountCells(right));
            for (int i = 0; i < n; i++)
            {
                if (IsCellChanged(GetCell(left, i), GetCell(right, i)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// テーブル行の表示用セル差分。Text のみ比較する。
        /// 交互行塗りなど Bg 差は「変更」にしない（一致セルは一致表示）。
        /// </summary>
        private static bool IsCellChanged(CellContent left, CellContent right)
        {
            string lt = left != null && left.Text != null ? left.Text : string.Empty;
            string rt = right != null && right.Text != null ? right.Text : string.Empty;
            return !string.Equals(lt, rt, StringComparison.Ordinal);
        }

        private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

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
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(new TextBlock
            {
                Text = "∅ この側になし（" + kind + "）",
                Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
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
            string mark = op == AlignOp.Match
                ? "＝"
                : (op == AlignOp.SkipLeft || op == AlignOp.SkipRight ? "±" : "·");
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
                case ContentBlockKind.TableHeader:
                    return "テーブル見出し";
                case ContentBlockKind.TableRow:
                    return "テーブル行";
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
                Brush onBrush = CreateCellChangeBrush();
                Brush offBrush = Frozen(0xFF, 0xF3, 0xF4, 0xF6);
                var cellBorder = new Border
                {
                    Margin = new Thickness(0, 0, 0, 3),
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = isDiff && _highlightVisible ? onBrush : offBrush,
                    BorderBrush = Frozen(0xFF, 0xE5, 0xE7, 0xEB),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Child = new TextBlock
                    {
                        Text = line,
                        Foreground = Frozen(0xFF, 0x11, 0x18, 0x27),
                        FontFamily = new FontFamily("Consolas, Yu Gothic UI, sans-serif"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                if (isDiff)
                {
                    _cellHighlightChromes.Add(new CellHighlightChrome
                    {
                        Target = cellBorder,
                        OnBrush = onBrush,
                        OffBrush = offBrush
                    });
                }

                panel.Children.Add(cellBorder);
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
                if (match)
                {
                    list.Add(d);
                }
            }

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
            if (_suppressVirtualize)
            {
                return;
            }

            // 仮想化・高さ補正で extent が変わっても、可視範囲の再計算は必要
            if (e.VerticalChange != 0 || e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0)
            {
                RealizeViewport(force: false);
            }

            if (_suppressScrollEvent)
            {
                return;
            }

            // 左右同期イベントは「オフセットが動いたとき」だけ。
            // ExtentHeightChange だけで ratio を飛ばすと:
            //   高さ実測 → スペーサ更新 → extent 変化 → 相手へ ratio 同期 → 再 Realize → 再実測
            // が微小量で無限に続き、待ち続けても終わらない。
            if (Math.Abs(e.VerticalChange) < 0.01)
            {
                return;
            }

            Action<double> handler = VerticalScrollRatioChanged;
            if (handler != null)
            {
                handler(GetVerticalScrollRatio());
            }
        }

        private void StreamScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.HeightChanged || e.WidthChanged)
            {
                RealizeViewport(force: true);
            }
        }

        /// <summary>
        /// チップは排他。本文は再構築しない。
        /// </summary>
        private void KindFilterChip_Click(object sender, RoutedEventArgs e)
        {
            StreamKindFilter next = StreamKindFilter.All;
            if (sender == ChipTable)
            {
                next = StreamKindFilter.Table;
            }
            else if (sender == ChipImage)
            {
                next = StreamKindFilter.Image;
            }
            else if (sender == ChipCell)
            {
                next = StreamKindFilter.Cell;
            }

            KindFilter = next;
        }

        /// <summary>
        /// チップの IsChecked を KindFilter に合わせる。
        /// </summary>
        private void SyncKindFilterChips()
        {
            SetChipChecked(ChipAll, _kindFilter == StreamKindFilter.All);
            SetChipChecked(ChipTable, _kindFilter == StreamKindFilter.Table);
            SetChipChecked(ChipImage, _kindFilter == StreamKindFilter.Image);
            SetChipChecked(ChipCell, _kindFilter == StreamKindFilter.Cell);
        }

        private static void SetChipChecked(ToggleButton chip, bool isChecked)
        {
            if (chip != null && chip.IsChecked != isChecked)
            {
                chip.IsChecked = isChecked;
            }
        }
    }

    /// <summary>
    /// 差分ペア index の集合と前後移動（WPF 非依存。ContentPane から利用）。
    /// </summary>
    public static class DiffPairNavigator
    {
        /// <summary>
        /// Skip 行 ∪ 非 Structure かつ StreamPairIndex ≥ 0 の index（昇順・重複なし）。
        /// </summary>
        public static IList<int> CollectDiffPairIndices(
            IList<ContentStreamPair> pairs,
            IEnumerable<DiffItem> items)
        {
            return CollectDiffPairIndices(pairs, items, StreamKindFilter.All);
        }

        /// <summary>
        /// Skip 行 ∪ 非 Structure かつ StreamPairIndex ≥ 0 の index（昇順・重複なし）。
        /// filter はジャンプ対象だけを絞る（高さマップは変えない）。
        /// </summary>
        public static IList<int> CollectDiffPairIndices(
            IList<ContentStreamPair> pairs,
            IEnumerable<DiffItem> items,
            StreamKindFilter filter)
        {
            var set = new SortedSet<int>();
            if (pairs != null)
            {
                for (int i = 0; i < pairs.Count; i++)
                {
                    if (pairs[i] != null && pairs[i].Op != AlignOp.Match)
                    {
                        set.Add(i);
                    }
                }
            }

            if (items != null)
            {
                foreach (DiffItem it in items)
                {
                    if (it == null || it.Kind == DiffKind.Structure || it.StreamPairIndex < 0)
                    {
                        continue;
                    }

                    if (pairs != null && it.StreamPairIndex >= pairs.Count)
                    {
                        continue;
                    }

                    set.Add(it.StreamPairIndex);
                }
            }

            var list = new List<int>(set.Count);
            foreach (int i in set)
            {
                if (!MatchesKindFilter(pairs, items, i, filter))
                {
                    continue;
                }

                list.Add(i);
            }

            return list;
        }

        /// <summary>
        /// pair ブロック種別または Table* / Image* DiffKind がフィルタに合うか。
        /// </summary>
        public static bool MatchesKindFilter(
            IList<ContentStreamPair> pairs,
            IEnumerable<DiffItem> items,
            int pairIndex,
            StreamKindFilter filter)
        {
            if (filter == StreamKindFilter.All)
            {
                return true;
            }

            if (pairs != null && pairIndex >= 0 && pairIndex < pairs.Count
                && PairMatchesFilter(pairs[pairIndex], filter))
            {
                return true;
            }

            if (items == null)
            {
                return false;
            }

            foreach (DiffItem it in items)
            {
                if (it == null || it.StreamPairIndex != pairIndex)
                {
                    continue;
                }

                if (ItemMatchesFilter(it, filter))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PairMatchesFilter(ContentStreamPair pair, StreamKindFilter filter)
        {
            if (pair == null)
            {
                return false;
            }

            return BlockMatchesFilter(pair.Left, filter) || BlockMatchesFilter(pair.Right, filter);
        }

        private static bool BlockMatchesFilter(ContentStreamBlock block, StreamKindFilter filter)
        {
            if (block == null)
            {
                return false;
            }

            if (filter == StreamKindFilter.Table)
            {
                return block.Kind == ContentBlockKind.Table
                    || block.Kind == ContentBlockKind.TableHeader
                    || block.Kind == ContentBlockKind.TableRow;
            }

            if (filter == StreamKindFilter.Image)
            {
                return block.Kind == ContentBlockKind.Image;
            }

            if (filter == StreamKindFilter.Cell)
            {
                return block.Kind == ContentBlockKind.LooseRow;
            }

            return true;
        }

        private static bool ItemMatchesFilter(DiffItem item, StreamKindFilter filter)
        {
            if (item == null)
            {
                return false;
            }

            if (filter == StreamKindFilter.Table)
            {
                return item.Kind == DiffKind.TableRowDelete
                    || item.Kind == DiffKind.TableRowInsert
                    || item.Kind == DiffKind.TableCellChange;
            }

            if (filter == StreamKindFilter.Image)
            {
                return item.Kind == DiffKind.Image
                    || item.Kind == DiffKind.ImageOnlyLeft
                    || item.Kind == DiffKind.ImageOnlyRight;
            }

            return false;
        }

        /// <summary>
        /// currentPairIndex の次／前の差分 index。端では循環。空なら -1。
        /// </summary>
        public static int PickNextDiffPairIndex(IList<int> indices, int currentPairIndex, int delta)
        {
            if (indices == null || indices.Count == 0)
            {
                return -1;
            }

            if (delta >= 0)
            {
                for (int i = 0; i < indices.Count; i++)
                {
                    if (indices[i] > currentPairIndex)
                    {
                        return indices[i];
                    }
                }

                return indices[0];
            }

            for (int i = indices.Count - 1; i >= 0; i--)
            {
                if (indices[i] < currentPairIndex)
                {
                    return indices[i];
                }
            }

            return indices[indices.Count - 1];
        }
    }
}
