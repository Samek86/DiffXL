using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// 現在シートの差分俯瞰 MiniMap（全シート横断はしない）。
    /// 1 シート分の帯内で ScrollRow を線形に配置する。
    /// </summary>
    public partial class MiniMapControl : UserControl
    {
        private readonly List<DiffItem> _items = new List<DiffItem>();
        /// <summary>描画・クリック用に並べた差分（OrderHint 順）。</summary>
        private List<DiffItem> _orderedItems = new List<DiffItem>();
        private readonly List<SheetSegment> _segments = new List<SheetSegment>();
        private List<string> _forcedSheetOrder = new List<string>();
        private bool _dragging;
        private string _viewportSheet = string.Empty;
        private int _viewportRow = 1;
        private int _viewportLeftRow = 1;
        private int _viewportRightRow = 1;
        private int _visibleRows = 28;
        private Rectangle _viewportBand;
        private TextBlock _viewportLabel;
        private ContentScrollMap _scrollMap;
        private int _mapMaxLeft = DefaultMaxRow;
        private int _mapMaxRight = DefaultMaxRow;
        /// <summary>内容ストリームの縦スクロール比率 0..1（青帯位置）。</summary>
        private double _contentViewportRatio;

        /// <summary>ビューポートが高さ全体に占める割合 0..1。</summary>
        private double _visibleFraction = 1;

        /// <summary>ドラッグ開始時の帯上端からの掴み位置。</summary>
        private double _grabOffset;

        /// <summary>直近描画した青帯の上端・高さ（ヒット用）。</summary>
        private double _lastBandTop;
        private double _lastBandH;

        /// <summary>シート名ヘッダは出さない（マップ全体をスクロール領域にする）。</summary>
        private const double SheetHeaderRatio = 0.0;

        /// <summary>現在シートの最低行スケール（ScrollRow との対応用）。</summary>
        private const int DefaultMaxRow = 120;

        private sealed class SheetSegment
        {
            public string Name;
            public double Top;
            public double Height;
            public int MinRow = 1;
            public int MaxRow = DefaultMaxRow;
            public int Index;
        }

        public MiniMapControl()
        {
            InitializeComponent();
            Loaded += (s, e) => Rebuild();
            IsHitTestVisible = true;
            Focusable = true;
            PreviewMouseLeftButtonDown += MiniMapControl_PreviewMouseLeftButtonDown;
            PreviewMouseMove += MiniMapControl_PreviewMouseMove;
            PreviewMouseLeftButtonUp += MiniMapControl_PreviewMouseLeftButtonUp;
        }

        public event Action<double, DiffItem> NavigateRequested;

        /// <summary>
        /// MiniMap ドラッグ開始（MouseDown / キャプチャ）。
        /// </summary>
        public event Action ScrubStarted;

        /// <summary>
        /// MiniMap ドラッグ終了（MouseUp / キャプチャ解除）。
        /// </summary>
        public event Action ScrubEnded;

        /// <summary>
        /// 内容マップ座標系でのナビ（ratio + 左右推奨行）。
        /// </summary>
        public event Action<double, int, int> NavigateMapped;

        /// <summary>直近ナビの推奨左行（Map 経由）。</summary>
        public int SuggestedLeftRow { get; private set; } = 1;

        /// <summary>直近ナビの推奨右行（Map 経由）。</summary>
        public int SuggestedRightRow { get; private set; } = 1;

        public void SetDiffs(IEnumerable<DiffItem> items)
        {
            _items.Clear();
            if (items != null)
            {
                _items.AddRange(items.Where(i => i != null));
            }

            // 呼び出し側が現在シート分だけ渡す前提。複数シート名が混在しても先頭 1 枚に縮約する。
            CollapseToSingleSheet();
            RebuildOrderedItems();
            RebuildSegments();
            UpdateHintText();
            ScheduleRebuild();
        }

        /// <summary>
        /// 内容ビューのスクロール比率と可視比率を青帯に反映する。
        /// </summary>
        public void SetContentViewport(double ratio, double visibleFraction)
        {
            _visibleFraction = MiniMapViewportBand.Clamp01(visibleFraction);
            SetContentViewportRatio(ratio);
        }

        /// <summary>
        /// 内容ビューのスクロール比率 0..1 を青帯に反映する。
        /// 高さは直近の可視比率を使う。
        /// </summary>
        public void SetContentViewportRatio(double ratio)
        {
            _contentViewportRatio = Math.Max(0, Math.Min(1, ratio));
            // 行番号ベースは使わず比率のみ
            UpdateViewportVisuals();
            UpdateHintText();
        }

        /// <summary>
        /// 描画・クリック用に差分を安定ソートする。
        /// </summary>
        private void RebuildOrderedItems()
        {
            _orderedItems = _items
                .Where(i => i != null)
                .OrderBy(i => i.OrderHint)
                .ThenBy(i => i.Kind.ToString(), StringComparer.Ordinal)
                .ThenBy(i => i.Summary ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(i => i.AddressLeft ?? i.AddressRight ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 現在シートのみを MiniMap に載せる（全シート横断は禁止）。
        /// 指定シート以外の Items は必ず落とし、帯も 1 枚に縮約する。
        /// </summary>
        /// <param name="sheetName">フォーカスシート名（左優先。片側のみは右名可）</param>
        /// <param name="items">候補差分（シート外は内部で除去）</param>
        public void SetCurrentSheet(string sheetName, IEnumerable<DiffItem> items)
        {
            string name = string.IsNullOrWhiteSpace(sheetName) ? string.Empty : sheetName.Trim();
            _items.Clear();
            if (items != null)
            {
                foreach (DiffItem item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    // シート名が分かる場合は厳密にそのシートのみ
                    if (!string.IsNullOrEmpty(name) && !ItemBelongsToSheet(item, name))
                    {
                        continue;
                    }

                    _items.Add(item);
                }
            }

            // ラベル: 指定名 → 差分から推定
            if (string.IsNullOrEmpty(name))
            {
                foreach (DiffItem item in _items)
                {
                    string s = item.SheetLeft ?? item.SheetRight;
                    if (!string.IsNullOrEmpty(s))
                    {
                        name = s;
                        break;
                    }
                }
            }

            _forcedSheetOrder = string.IsNullOrEmpty(name)
                ? new List<string>()
                : new List<string> { name };
            _viewportSheet = name;
            // 防御: 混入した他シート差分・複数帯を必ず 1 シートに縮約
            CollapseToSingleSheet();
            RebuildOrderedItems();
            RebuildSegments();
            UpdateHintText();
            ScheduleRebuild();
        }

        /// <summary>
        /// シート表示（現在シート 1 枚のみ。複数指定されても先頭を採用）。
        /// </summary>
        public void SetSheetOrder(IEnumerable<string> sheetNames)
        {
            string first = (sheetNames ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .FirstOrDefault();
            _forcedSheetOrder = string.IsNullOrEmpty(first)
                ? new List<string>()
                : new List<string> { first };
            if (!string.IsNullOrEmpty(first))
            {
                _viewportSheet = first;
            }

            CollapseToSingleSheet();
            RebuildSegments();
            ScheduleRebuild();
        }

        /// <summary>
        /// 現在シートの Alignment（ScrollMap + シート名）を適用する。
        /// </summary>
        public void SetAlignment(SheetAlignment alignment)
        {
            if (alignment == null)
            {
                SetScrollMap(null);
                return;
            }

            string sheet = !string.IsNullOrWhiteSpace(alignment.LeftSheet)
                ? alignment.LeftSheet.Trim()
                : (alignment.RightSheet ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(sheet))
            {
                // 現在シート 1 枚に差し替え（追加しない）
                _forcedSheetOrder = new List<string> { sheet };
                _viewportSheet = sheet;
            }

            SetScrollMap(alignment.ScrollMap);
        }

        /// <summary>
        /// 内容スクロールマップを直接設定する。
        /// </summary>
        public void SetScrollMap(ContentScrollMap map)
        {
            _scrollMap = map;
            EstimateMapExtents(map, out _mapMaxLeft, out _mapMaxRight);
            RebuildSegments();
            ScheduleRebuild();
        }

        public void Clear()
        {
            _items.Clear();
            _segments.Clear();
            _forcedSheetOrder.Clear();
            _scrollMap = null;
            _mapMaxLeft = DefaultMaxRow;
            _mapMaxRight = DefaultMaxRow;
            MapCanvas.Children.Clear();
            _viewportBand = null;
            _viewportLabel = null;
            _viewportSheet = string.Empty;
            _viewportRow = 1;
            _viewportLeftRow = 1;
            _viewportRightRow = 1;
            SuggestedLeftRow = 1;
            SuggestedRightRow = 1;
            HintText.Text = "クリックで移動";
        }

        public void RefreshStyle()
        {
            Rebuild();
        }

        /// <summary>
        /// 本文の ScrollRow とシート名で青帯を更新する（本文と同じ基準）。
        /// </summary>
        public void SetViewport(string sheetName, int scrollRow, int visibleRows = 28)
        {
            _viewportSheet = sheetName ?? string.Empty;
            _viewportRow = Math.Max(1, scrollRow);
            _viewportLeftRow = _viewportRow;
            // マップがあれば右は対応行、なければ同値（後方互換）
            _viewportRightRow = MapLeftRowToRight(_viewportLeftRow);
            _visibleRows = Math.Max(8, Math.Min(60, visibleRows));

            // 未知シートなら帯を追加して順序を崩さないよう末尾に
            if (!string.IsNullOrEmpty(_viewportSheet) && FindSegment(_viewportSheet) == null)
            {
                EnsureSheetInOrder(_viewportSheet);
                RebuildSegments();
                ScheduleRebuild();
            }

            UpdateViewportVisuals();
            UpdateHintText();
        }

        /// <summary>
        /// 左右の内容対応行で青帯を更新する（ラベルは L{n} · R{m}）。
        /// </summary>
        public void SetViewportMapped(int leftRow, int rightRow)
        {
            _viewportLeftRow = Math.Max(1, leftRow);
            _viewportRightRow = Math.Max(1, rightRow);
            _viewportRow = _viewportLeftRow;
            UpdateViewportVisuals();
            UpdateHintText();
        }

        /// <summary>
        /// シート名付きで左右対応行の青帯を更新する。
        /// </summary>
        public void SetViewportMapped(string sheetName, int leftRow, int rightRow, int visibleRows = 28)
        {
            _viewportSheet = sheetName ?? string.Empty;
            _visibleRows = Math.Max(8, Math.Min(60, visibleRows));
            if (!string.IsNullOrEmpty(_viewportSheet) && FindSegment(_viewportSheet) == null)
            {
                EnsureSheetInOrder(_viewportSheet);
                RebuildSegments();
                ScheduleRebuild();
            }

            SetViewportMapped(leftRow, rightRow);
        }

        public void SetViewport(double orderHint, string sheetName, int scrollRow, int visibleRows = 28)
        {
            SetViewport(sheetName, scrollRow, visibleRows);
        }

        public void SetViewportRatio(double ratio)
        {
            SetContentViewportRatio(ratio);
        }

        private void ScheduleRebuild()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Rebuild));
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(Rebuild));
        }

        private void EnsureSheetInOrder(string sheet)
        {
            if (string.IsNullOrWhiteSpace(sheet))
            {
                return;
            }

            // 現在シートのみ: 常に 1 枚へ差し替え（スタックしない）
            string name = sheet.Trim();
            if (_forcedSheetOrder.Count == 1
                && string.Equals(_forcedSheetOrder[0], name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _forcedSheetOrder = new List<string> { name };
            _viewportSheet = name;
        }

        /// <summary>
        /// 複数シート名が混ざった差分を、強制順 or 先頭出現の 1 シート分に縮約する。
        /// </summary>
        private void CollapseToSingleSheet()
        {
            string current = _forcedSheetOrder != null && _forcedSheetOrder.Count > 0
                ? _forcedSheetOrder[0]
                : null;
            if (string.IsNullOrEmpty(current))
            {
                foreach (DiffItem item in _items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    string s = item.SheetLeft ?? item.SheetRight;
                    if (!string.IsNullOrEmpty(s))
                    {
                        current = s;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(current))
            {
                _forcedSheetOrder = new List<string>();
                return;
            }

            _forcedSheetOrder = new List<string> { current };
            if (_items.Count > 0)
            {
                _items.RemoveAll(i => i == null || !ItemBelongsToSheet(i, current));
            }
        }

        private static bool ItemBelongsToSheet(DiffItem item, string sheetName)
        {
            if (item == null || string.IsNullOrEmpty(sheetName))
            {
                return false;
            }

            if (string.Equals(item.SheetLeft, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(item.SheetRight, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 片側 Structure 等: 片方が空でももう一方が一致
            if (string.IsNullOrEmpty(item.SheetLeft)
                && string.Equals(item.SheetRight, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrEmpty(item.SheetRight)
                && string.Equals(item.SheetLeft, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private void RebuildSegments()
        {
            List<string> names = ResolveSheetNameOrder();
            // 現在シートのみ（2 枚以上は先頭のみ）
            if (names.Count > 1)
            {
                names = new List<string> { names[0] };
                _forcedSheetOrder = new List<string> { names[0] };
            }

            if (names.Count == 0)
            {
                _segments.Clear();
                return;
            }

            // ContentScrollMap の Lmax を優先し、差分行・既定値で下駄を履かせる
            int globalMax = Math.Max(DefaultMaxRow, Math.Max(_mapMaxLeft, _mapMaxRight));
            foreach (DiffItem item in _items)
            {
                int r = GetItemRow(item);
                if (r > globalMax)
                {
                    globalMax = r;
                }
            }

            globalMax = Math.Max(DefaultMaxRow, globalMax + 20);

            _segments.Clear();
            // 単一シートで全高を使う
            _segments.Add(new SheetSegment
            {
                Name = names[0],
                Top = 0,
                Height = 1.0,
                MinRow = 1,
                MaxRow = globalMax,
                Index = 0
            });
        }

        /// <summary>
        /// ScrollMap から内容行の上限を推定する。
        /// </summary>
        private static void EstimateMapExtents(ContentScrollMap map, out int maxLeft, out int maxRight)
        {
            maxLeft = DefaultMaxRow;
            maxRight = DefaultMaxRow;
            if (map == null || !map.IsContentBased)
            {
                return;
            }

            // Describe を解析せず、Map でサンプルして上限を探る
            int probeLeft = 1;
            int probeRight = 1;
            for (int r = 1; r <= 2000; r += 25)
            {
                int mappedR = map.MapLeftToRight(r);
                int mappedL = map.MapRightToLeft(r);
                if (mappedR > probeRight)
                {
                    probeRight = mappedR;
                }

                if (mappedL > probeLeft)
                {
                    probeLeft = mappedL;
                }

                if (r > probeLeft)
                {
                    probeLeft = r;
                }

                if (r > probeRight)
                {
                    probeRight = r;
                }
            }

            maxLeft = Math.Max(DefaultMaxRow, probeLeft + 20);
            maxRight = Math.Max(DefaultMaxRow, probeRight + 20);
        }

        private int MapLeftRowToRight(int leftRow)
        {
            leftRow = Math.Max(1, leftRow);
            if (_scrollMap == null)
            {
                return leftRow;
            }

            return Math.Max(1, _scrollMap.MapLeftToRight(leftRow));
        }

        /// <summary>
        /// シート帯内の比率から左右行を解決する（左を主、右は Map）。
        /// </summary>
        private void ResolveRowsFromRatio(SheetSegment seg, double ratio, out int leftRow, out int rightRow)
        {
            // 論理 t ∈ [0,1] をシート本文に射影
            int lmax = Math.Max(seg.MaxRow, _mapMaxLeft);
            double bodyTop = seg.Top + seg.Height * SheetHeaderRatio;
            double bodyH = Math.Max(0.0001, seg.Height * (1.0 - SheetHeaderRatio));
            double local = (ratio - bodyTop) / bodyH;
            local = Math.Max(0, Math.Min(1, local));
            leftRow = Math.Max(1, 1 + (int)Math.Round(local * Math.Max(0, lmax - 1)));
            // 従来スケールとも整合（セグメント MaxRow）
            int classic = RatioToRow(seg, ratio);
            // マップ有効時は Lmax 基準、なければ classic
            if (_scrollMap != null && _scrollMap.IsContentBased)
            {
                leftRow = Math.Max(1, 1 + (int)Math.Round(local * Math.Max(0, lmax - 1)));
            }
            else
            {
                leftRow = classic;
            }

            rightRow = MapLeftRowToRight(leftRow);
            SuggestedLeftRow = leftRow;
            SuggestedRightRow = rightRow;
        }

        /// <summary>
        /// 現在シート名（強制 1 枚 → なければ差分の先頭シート）。
        /// </summary>
        private List<string> ResolveSheetNameOrder()
        {
            if (_forcedSheetOrder != null && _forcedSheetOrder.Count > 0)
            {
                return new List<string> { _forcedSheetOrder[0] };
            }

            foreach (DiffItem item in _items.OrderBy(i => i.OrderHint).ThenBy(i => i.SheetLeft ?? i.SheetRight ?? string.Empty))
            {
                string s = item.SheetLeft ?? item.SheetRight ?? string.Empty;
                if (!string.IsNullOrEmpty(s))
                {
                    return new List<string> { s };
                }
            }

            if (!string.IsNullOrEmpty(_viewportSheet))
            {
                return new List<string> { _viewportSheet };
            }

            return new List<string>();
        }

        private static int GetItemRow(DiffItem item)
        {
            if (item == null)
            {
                return 0;
            }

            int r = TextDiffService.ParseAnchorRow(item.AddressLeft);
            if (r <= 0)
            {
                r = TextDiffService.ParseAnchorRow(item.AddressRight);
            }

            if (r > 0)
            {
                return r;
            }

            // OrderHint は row*1000+col または pair*1000+offset。行は千の位以上を使う。
            if (item.OrderHint > 0)
            {
                int oh = (int)Math.Round(item.OrderHint);
                int asRow = oh / 1000;
                if (asRow > 0)
                {
                    return asRow;
                }

                // 小さい値はそのまま行番号扱い
                return Math.Max(1, oh);
            }

            return 0;
        }

        private void Rebuild()
        {
            MapCanvas.Children.Clear();
            _viewportBand = null;
            _viewportLabel = null;

            double h = MapBorder.ActualHeight;
            double w = MapBorder.ActualWidth;
            if (h < 10 || w < 4)
            {
                if (_segments.Count > 0 || _items.Count > 0)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Rebuild));
                }

                return;
            }

            MapCanvas.Width = w;
            MapCanvas.Height = h;

            if (_segments.Count == 0)
            {
                RebuildSegments();
            }

            DrawSheetSegments(w, h);
            DrawMarkers(w, h);
            UpdateViewportVisuals();
        }

        private void DrawSheetSegments(double w, double h)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                SheetSegment seg = _segments[i];
                double y = seg.Top * h;
                double sh = Math.Max(10, seg.Height * h);

                // 本文のみ（シート名ヘッダ・ラベルは出さない）
                var body = new Rectangle
                {
                    Width = w,
                    Height = sh,
                    Fill = new SolidColorBrush(Color.FromRgb(
                        (byte)(i % 2 == 0 ? 0xF3 : 0xE5),
                        (byte)(i % 2 == 0 ? 0xF4 : 0xE7),
                        (byte)(i % 2 == 0 ? 0xF6 : 0xEB))),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(body, 0);
                Canvas.SetTop(body, y);
                MapCanvas.Children.Add(body);

                if (i > 0)
                {
                    var sep = new Line
                    {
                        X1 = 0,
                        X2 = w,
                        Y1 = y,
                        Y2 = y,
                        Stroke = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
                        StrokeThickness = 2.5,
                        IsHitTestVisible = false
                    };
                    MapCanvas.Children.Add(sep);
                }
            }
        }

        private void DrawMarkers(double w, double h)
        {
            if (_orderedItems == null || _orderedItems.Count == 0)
            {
                RebuildOrderedItems();
            }

            var fill = new SolidColorBrush(Color.FromArgb(240, 250, 204, 21));
            fill.Freeze();
            var stroke = new SolidColorBrush(Color.FromRgb(202, 138, 4));
            stroke.Freeze();
            double markLeft = 4;
            double markW = Math.Max(8, w - 8);
            int n = _orderedItems.Count;
            if (n == 0)
            {
                return;
            }

            // 内容ストリーム方式: 差分を上から等間隔に並べる（Excel 行番号は使わない）
            double markerH = Math.Max(6, Math.Min(14, h * 0.035));
            for (int i = 0; i < n; i++)
            {
                DiffItem item = _orderedItems[i];
                if (item == null)
                {
                    continue;
                }

                double y = IndexToCanvasY(i, n, h, markerH);
                var rect = new Rectangle
                {
                    Width = markW,
                    Height = markerH,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 1.5,
                    Tag = new MiniMapMarkerTag { Item = item, Index = i },
                    ToolTip = BuildTooltip(item),
                    // ヒットは MapBorder に任せ、スクロールバー同様ドラッグを阻害しない
                    IsHitTestVisible = false,
                    Cursor = Cursors.Hand
                };
                Canvas.SetLeft(rect, markLeft);
                Canvas.SetTop(rect, y);
                Panel.SetZIndex(rect, 20);
                MapCanvas.Children.Add(rect);
            }
        }

        /// <summary>
        /// 差分 index → Canvas Y（本文領域内の等間隔配置）。
        /// </summary>
        private static double IndexToCanvasY(int index, int count, double canvasH, double markerH)
        {
            double bodyTop = canvasH * SheetHeaderRatio;
            double bodyH = Math.Max(1, canvasH * (1.0 - SheetHeaderRatio));
            if (count <= 0)
            {
                return bodyTop;
            }

            double t = (index + 0.5) / count;
            double center = bodyTop + t * bodyH;
            return Math.Max(bodyTop, Math.Min(canvasH - markerH, center - markerH * 0.5));
        }

        /// <summary>
        /// Canvas Y → 差分 index。
        /// </summary>
        private static int CanvasYToIndex(double y, double canvasH, int count)
        {
            if (count <= 0)
            {
                return -1;
            }

            double bodyTop = canvasH * SheetHeaderRatio;
            double bodyH = Math.Max(1, canvasH * (1.0 - SheetHeaderRatio));
            double local = (y - bodyTop) / bodyH;
            local = Math.Max(0, Math.Min(0.999999, local));
            int idx = (int)(local * count);
            if (idx < 0)
            {
                idx = 0;
            }

            if (idx >= count)
            {
                idx = count - 1;
            }

            return idx;
        }

        /// <summary>
        /// マーカー用タグ。
        /// </summary>
        private sealed class MiniMapMarkerTag
        {
            public DiffItem Item;
            public int Index;
        }

        private int SegmentIndexOf(DiffItem item)
        {
            string sheet = item != null ? (item.SheetLeft ?? item.SheetRight ?? string.Empty) : string.Empty;
            SheetSegment seg = FindSegment(sheet);
            return seg != null ? seg.Index : 999;
        }

        private bool TryMapItemToRatio(DiffItem item, out double ratio)
        {
            ratio = 0;
            if (item == null || _segments.Count == 0)
            {
                return false;
            }

            // 現在フォーカス帯以外のシート差分は描画しない
            string focus = _segments[0].Name;
            if (!string.IsNullOrEmpty(focus) && !ItemBelongsToSheet(item, focus))
            {
                return false;
            }

            int row = GetItemRow(item);
            if (row <= 0)
            {
                row = 1;
            }

            ratio = MapRowToRatio(_segments[0], row);
            return true;
        }

        private SheetSegment FindSegment(string sheet)
        {
            if (_segments.Count == 0)
            {
                return null;
            }

            if (string.IsNullOrEmpty(sheet))
            {
                return _segments[0];
            }

            foreach (SheetSegment s in _segments)
            {
                if (string.Equals(s.Name, sheet, StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }

            return null;
        }

        private SheetSegment ResolveSegmentByRatio(double ratio)
        {
            foreach (SheetSegment s in _segments)
            {
                if (ratio >= s.Top && ratio <= s.Top + s.Height + 0.0001)
                {
                    return s;
                }
            }

            return ratio < 0.5 ? _segments[0] : _segments[_segments.Count - 1];
        }

        /// <summary>
        /// ScrollRow → MiniMap 全体比率（ヘッダを避け本文のみ）。
        /// </summary>
        private static double MapRowToRatio(SheetSegment seg, int row)
        {
            int span = Math.Max(1, seg.MaxRow - seg.MinRow);
            double local = (Math.Max(seg.MinRow, row) - seg.MinRow) / (double)span;
            local = Math.Max(0, Math.Min(1, local));
            double bodyTop = seg.Top + seg.Height * SheetHeaderRatio;
            double bodyH = seg.Height * (1.0 - SheetHeaderRatio);
            return bodyTop + local * bodyH;
        }

        private static int RatioToRow(SheetSegment seg, double ratio)
        {
            double bodyTop = seg.Top + seg.Height * SheetHeaderRatio;
            double bodyH = Math.Max(0.0001, seg.Height * (1.0 - SheetHeaderRatio));
            double local = (ratio - bodyTop) / bodyH;
            local = Math.Max(0, Math.Min(1, local));
            int span = Math.Max(1, seg.MaxRow - seg.MinRow);
            return Math.Max(1, seg.MinRow + (int)Math.Round(local * span));
        }

        private void UpdateViewportVisuals()
        {
            double h = MapBorder.ActualHeight;
            double w = MapBorder.ActualWidth;
            if (h < 10 || w < 4)
            {
                return;
            }

            // 内容ストリーム比率ベースの青帯（Excel 行は使わない）
            double bodyTop = h * SheetHeaderRatio;
            double bodyH = Math.Max(8, h * (1.0 - SheetHeaderRatio));
            double bandH = MiniMapViewportBand.BandHeight(bodyH, _visibleFraction);
            double y = MiniMapViewportBand.BandTop(bodyTop, bodyH, bandH, _contentViewportRatio);
            _lastBandTop = y;
            _lastBandH = bandH;

            if (_viewportBand == null)
            {
                _viewportBand = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(250, 37, 99, 235)),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(70, 59, 130, 246)),
                    IsHitTestVisible = false
                };
                MapCanvas.Children.Add(_viewportBand);
            }

            _viewportBand.Width = Math.Max(8, w - 4);
            _viewportBand.Height = bandH;
            Canvas.SetLeft(_viewportBand, 2);
            Canvas.SetTop(_viewportBand, y);
            Panel.SetZIndex(_viewportBand, 50);
            if (!MapCanvas.Children.Contains(_viewportBand))
            {
                MapCanvas.Children.Add(_viewportBand);
            }

            if (_viewportLabel == null)
            {
                _viewportLabel = new TextBlock
                {
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x8A)),
                    Background = new SolidColorBrush(Color.FromArgb(230, 0xDB, 0xEA, 0xFE)),
                    Padding = new Thickness(4, 1, 4, 1),
                    IsHitTestVisible = false
                };
                MapCanvas.Children.Add(_viewportLabel);
            }

            // ラベル: スクロール%のみ（シート名は出さない）
            int pct = (int)Math.Round(_contentViewportRatio * 100);
            _viewportLabel.Text = pct + "%";
            _viewportLabel.Visibility = bandH >= MiniMapViewportBand.LabelMinBandHeightPx
                ? Visibility.Visible
                : Visibility.Collapsed;

            Canvas.SetLeft(_viewportLabel, 3);
            Canvas.SetTop(_viewportLabel, Math.Max(0, y + 2));
            Panel.SetZIndex(_viewportLabel, 51);
            if (!MapCanvas.Children.Contains(_viewportLabel))
            {
                MapCanvas.Children.Add(_viewportLabel);
            }
        }

        private void UpdateHintText()
        {
            if (_orderedItems == null || _orderedItems.Count == 0)
            {
                RebuildOrderedItems();
            }

            if (_orderedItems.Count == 0)
            {
                HintText.Text = "差分なし\nドラッグでスクロール";
                return;
            }

            int pct = (int)Math.Round(_contentViewportRatio * 100);
            HintText.Text = "差分 " + _orderedItems.Count + " 件\n"
                + "表示 " + pct + "%\n"
                + "ドラッグでスクロール";
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
            {
                return s ?? string.Empty;
            }

            return s.Substring(0, max - 1) + "…";
        }

        private static string BuildTooltip(DiffItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            // シート名は表示しない（種別・要約・番地のみ）
            string addr = item.AddressLeft ?? item.AddressRight ?? string.Empty;
            string body = item.Summary ?? item.Kind.ToString();
            if (string.IsNullOrEmpty(addr))
            {
                return body;
            }

            return body + "\n" + addr;
        }

        /// <summary>
        /// ポインタ Y から内容スクロール比率 0..1 を計算する（スクロールバー同等）。
        /// </summary>
        private double PointToContentRatio(Point p)
        {
            double h = Math.Max(1, MapBorder.ActualHeight);
            double bodyTop = h * SheetHeaderRatio;
            double bodyH = Math.Max(1, h * (1.0 - SheetHeaderRatio));
            double bandH = MiniMapViewportBand.BandHeight(bodyH, _visibleFraction);
            return MiniMapViewportBand.RatioFromPointer(p.Y, _grabOffset, bodyTop, bodyH, bandH);
        }

        /// <summary>
        /// ダウン時点の掴み位置を決める（帯内=相対、帯外=中心ジャンプ）。
        /// </summary>
        private void CaptureGrab(Point p)
        {
            double h = Math.Max(1, MapBorder.ActualHeight);
            double bodyTop = h * SheetHeaderRatio;
            double bodyH = Math.Max(1, h * (1.0 - SheetHeaderRatio));
            double bandH = MiniMapViewportBand.BandHeight(bodyH, _visibleFraction);
            double bandTop = MiniMapViewportBand.BandTop(bodyTop, bodyH, bandH, _contentViewportRatio);
            _lastBandTop = bandTop;
            _lastBandH = bandH;
            if (MiniMapViewportBand.HitTestThumb(p.Y, bandTop, bandH))
            {
                _grabOffset = p.Y - bandTop;
            }
            else
            {
                _grabOffset = bandH * 0.5;
            }
        }

        private void RaiseNavigate(Point p)
        {
            if (_orderedItems == null || _orderedItems.Count == 0)
            {
                RebuildOrderedItems();
            }

            // スクロールバー同様: Y 位置 = 内容のスクロール比率（差分 index に丸めない）
            double contentRatio = PointToContentRatio(p);

            DiffItem item = null;
            int n = _orderedItems != null ? _orderedItems.Count : 0;
            if (n > 0)
            {
                int idx = CanvasYToIndex(p.Y, Math.Max(1, MapBorder.ActualHeight), n);
                if (idx >= 0 && idx < n)
                {
                    item = _orderedItems[idx];
                }
            }

            _contentViewportRatio = contentRatio;
            UpdateViewportVisuals();
            UpdateHintText();

            SuggestedLeftRow = 1 + (int)Math.Round(contentRatio * 100);
            SuggestedRightRow = SuggestedLeftRow;
            NavigateMapped?.Invoke(contentRatio, SuggestedLeftRow, SuggestedRightRow);
            // 第2引数はヒント用。スクロール本体は常に ratio
            NavigateRequested?.Invoke(contentRatio, item);
        }

        private DiffItem FindNearestOnSheet(string sheet, int row)
        {
            DiffItem best = null;
            int bestDist = int.MaxValue;
            foreach (DiffItem item in _items)
            {
                string sn = item.SheetLeft ?? item.SheetRight ?? string.Empty;
                if (!string.Equals(sn, sheet, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int r = GetItemRow(item);
                if (r <= 0)
                {
                    continue;
                }

                int d = Math.Abs(r - row);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = item;
                }
            }

            return best;
        }

        private void MiniMapControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // MapBorder 外（タイトル等）は無視
            if (MapBorder == null)
            {
                return;
            }

            Point p = e.GetPosition(MapBorder);
            if (p.X < 0 || p.Y < 0 || p.X > MapBorder.ActualWidth || p.Y > MapBorder.ActualHeight)
            {
                return;
            }

            CaptureGrab(p);
            BeginScrub();
            RaiseNavigate(p);
            e.Handled = true;
        }

        private void MiniMapControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            if (MapBorder == null)
            {
                return;
            }

            Point p = e.GetPosition(MapBorder);
            // ドラッグ中は領域外でも Y をクランプしてスクロール継続
            p.Y = Math.Max(0, Math.Min(MapBorder.ActualHeight, p.Y));
            p.X = Math.Max(0, Math.Min(MapBorder.ActualWidth, p.X));
            RaiseNavigate(p);
            e.Handled = true;
        }

        private void MiniMapControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            // 最終位置を 1 回出してから終了
            if (MapBorder != null)
            {
                Point p = e.GetPosition(MapBorder);
                p.Y = Math.Max(0, Math.Min(MapBorder.ActualHeight, p.Y));
                p.X = Math.Max(0, Math.Min(MapBorder.ActualWidth, p.X));
                RaiseNavigate(p);
            }

            EndScrub();
            e.Handled = true;
        }

        private void MapBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(MapBorder);
            CaptureGrab(p);
            BeginScrub();
            RaiseNavigate(p);
            e.Handled = true;
        }

        private void MapBorder_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point p = e.GetPosition(MapBorder);
                p.Y = Math.Max(0, Math.Min(MapBorder.ActualHeight, p.Y));
                RaiseNavigate(p);
                e.Handled = true;
            }
        }

        private void MapBorder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragging)
            {
                if (MapBorder != null)
                {
                    Point p = e.GetPosition(MapBorder);
                    p.Y = Math.Max(0, Math.Min(MapBorder.ActualHeight, p.Y));
                    RaiseNavigate(p);
                }

                EndScrub();
                e.Handled = true;
            }
        }

        private void BeginScrub()
        {
            if (_dragging)
            {
                return;
            }

            _dragging = true;
            Focus();
            CaptureMouse();
            Action started = ScrubStarted;
            if (started != null)
            {
                started();
            }
        }

        private void EndScrub()
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            Action ended = ScrubEnded;
            if (ended != null)
            {
                ended();
            }
        }

        private void MapBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Rebuild();
        }
    }
}
