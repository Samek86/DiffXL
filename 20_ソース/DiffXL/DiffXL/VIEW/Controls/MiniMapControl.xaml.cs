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

        /// <summary>シート帯の上部をシート名ヘッダに使う比率。</summary>
        private const double SheetHeaderRatio = 0.12;

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
            RebuildSegments();
            UpdateHintText();
            ScheduleRebuild();
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
            if (_segments.Count == 0)
            {
                return;
            }

            ratio = Math.Max(0, Math.Min(1, ratio));
            SheetSegment seg = ResolveSegmentByRatio(ratio);
            int leftRow;
            int rightRow;
            ResolveRowsFromRatio(seg, ratio, out leftRow, out rightRow);
            SetViewportMapped(seg.Name, leftRow, rightRow, _visibleRows);
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
                double headerH = Math.Max(16, sh * SheetHeaderRatio);
                double bodyH = Math.Max(4, sh - headerH);

                // 本文（マーカー領域）— ライト
                var body = new Rectangle
                {
                    Width = w,
                    Height = bodyH,
                    Fill = new SolidColorBrush(Color.FromRgb(
                        (byte)(i % 2 == 0 ? 0xF3 : 0xE5),
                        (byte)(i % 2 == 0 ? 0xF4 : 0xE7),
                        (byte)(i % 2 == 0 ? 0xF6 : 0xEB))),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(body, 0);
                Canvas.SetTop(body, y + headerH);
                MapCanvas.Children.Add(body);

                // シート名ヘッダ（マーカー禁止）
                var header = new Rectangle
                {
                    Width = w,
                    Height = headerH,
                    Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0xE7, 0xFF)),
                    Stroke = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(header, 0);
                Canvas.SetTop(header, y);
                MapCanvas.Children.Add(header);

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

                // 現在シート名（単一）
                var label = new TextBlock
                {
                    Text = Truncate(seg.Name, 10),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x8A)),
                    ToolTip = seg.Name,
                    IsHitTestVisible = false,
                    Width = Math.Max(20, w - 6),
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(label, 3);
                Canvas.SetTop(label, y + Math.Max(1, (headerH - 14) / 2));
                MapCanvas.Children.Add(label);
            }
        }

        private void DrawMarkers(double w, double h)
        {
            var fill = new SolidColorBrush(Color.FromArgb(240, 250, 204, 21));
            fill.Freeze();
            var stroke = new SolidColorBrush(Color.FromRgb(202, 138, 4));
            stroke.Freeze();
            double markLeft = 4;
            double markW = Math.Max(8, w - 8);

            // 現在帯内は行順（異名ペアでも単一帯に投影）
            foreach (DiffItem item in _items
                .OrderBy(i => GetItemRow(i))
                .ThenBy(i => i.AddressLeft ?? i.AddressRight ?? string.Empty))
            {
                double ratio;
                if (!TryMapItemToRatio(item, out ratio))
                {
                    continue;
                }

                double markerH = Math.Max(4, Math.Min(9, h * 0.012));
                double y = ratio * Math.Max(1, h - markerH);
                var rect = new Rectangle
                {
                    Width = markW,
                    Height = markerH,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 1,
                    Tag = item,
                    ToolTip = BuildTooltip(item),
                    IsHitTestVisible = true,
                    Cursor = Cursors.Hand
                };
                rect.MouseLeftButtonDown += Marker_MouseLeftButtonDown;
                Canvas.SetLeft(rect, markLeft);
                Canvas.SetTop(rect, y);
                MapCanvas.Children.Add(rect);
            }
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
            if (h < 10 || w < 4 || _segments.Count == 0)
            {
                return;
            }

            SheetSegment seg = FindSegment(_viewportSheet) ?? _segments[0];
            double topRatio = MapRowToRatio(seg, _viewportRow);
            double endRatio = MapRowToRatio(seg, _viewportRow + _visibleRows);
            double bandH = Math.Max(10, Math.Abs(endRatio - topRatio) * h);
            double bodyHpx = Math.Max(8, seg.Height * (1.0 - SheetHeaderRatio) * h);
            bandH = Math.Max(bodyHpx * 0.15, Math.Min(bodyHpx * 0.7, bandH));

            double y = topRatio * h;
            double bodyBottom = (seg.Top + seg.Height) * h;
            double bodyTop = (seg.Top + seg.Height * SheetHeaderRatio) * h;
            if (y < bodyTop)
            {
                y = bodyTop;
            }

            if (y + bandH > bodyBottom)
            {
                y = Math.Max(bodyTop, bodyBottom - bandH);
            }

            if (_viewportBand == null)
            {
                _viewportBand = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(250, 96, 165, 250)),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(80, 59, 130, 246)),
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

            // ラベル: L7 · R9 形式（内容対応で左右が異なるとき一目で分かる）
            int lRow = Math.Max(1, _viewportLeftRow > 0 ? _viewportLeftRow : _viewportRow);
            int rRow = Math.Max(1, _viewportRightRow > 0 ? _viewportRightRow : lRow);
            string sheetShort = Truncate(string.IsNullOrEmpty(_viewportSheet) ? seg.Name : _viewportSheet, 6);
            if (lRow == rRow)
            {
                _viewportLabel.Text = sheetShort + " · L" + lRow + " · R" + rRow;
            }
            else
            {
                _viewportLabel.Text = "L" + lRow + " · R" + rRow;
            }

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
            if (_items.Count == 0 && _segments.Count == 0)
            {
                HintText.Text = "差分なし";
                return;
            }

            string sheet = string.IsNullOrEmpty(_viewportSheet) ? "—" : _viewportSheet;
            int lRow = Math.Max(1, _viewportLeftRow > 0 ? _viewportLeftRow : _viewportRow);
            int rRow = Math.Max(1, _viewportRightRow > 0 ? _viewportRightRow : lRow);
            HintText.Text = "差分 " + _items.Count + " 件\n"
                + sheet + "\n"
                + "L" + lRow + " · R" + rRow + "\n"
                + "クリックで移動";
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

            string sheet = item.SheetLeft ?? item.SheetRight ?? string.Empty;
            string addr = item.AddressLeft ?? item.AddressRight ?? string.Empty;
            string head = string.IsNullOrEmpty(sheet)
                ? addr
                : (string.IsNullOrEmpty(addr) ? sheet : sheet + "!" + addr);
            string body = item.Summary ?? item.Kind.ToString();
            return string.IsNullOrEmpty(head) ? body : head + "\n" + body;
        }

        private void RaiseNavigate(Point p)
        {
            double h = Math.Max(1, MapBorder.ActualHeight);
            double ratio = Math.Max(0, Math.Min(1, p.Y / h));
            if (_segments.Count == 0)
            {
                int fallback = Math.Max(1, 1 + (int)Math.Round(ratio * (DefaultMaxRow - 1)));
                SuggestedLeftRow = fallback;
                SuggestedRightRow = MapLeftRowToRight(fallback);
                NavigateMapped?.Invoke(ratio, SuggestedLeftRow, SuggestedRightRow);
                NavigateRequested?.Invoke(ratio, null);
                return;
            }

            SheetSegment seg = ResolveSegmentByRatio(ratio);
            int leftRow;
            int rightRow;
            ResolveRowsFromRatio(seg, ratio, out leftRow, out rightRow);
            _viewportSheet = seg.Name;
            _viewportRow = leftRow;
            _viewportLeftRow = leftRow;
            _viewportRightRow = rightRow;
            UpdateViewportVisuals();
            UpdateHintText();

            NavigateMapped?.Invoke(ratio, leftRow, rightRow);
            DiffItem nearest = FindNearestOnSheet(seg.Name, leftRow);
            NavigateRequested?.Invoke(ratio, nearest);
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

        private void Marker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var rect = sender as Rectangle;
            var item = rect != null ? rect.Tag as DiffItem : null;
            if (item == null)
            {
                return;
            }

            double ratio;
            TryMapItemToRatio(item, out ratio);
            string sheet = item.SheetLeft ?? item.SheetRight ?? string.Empty;
            int leftRow = TextDiffService.ParseAnchorRow(item.AddressLeft);
            int rightRow = TextDiffService.ParseAnchorRow(item.AddressRight);
            if (leftRow <= 0)
            {
                leftRow = GetItemRow(item);
            }

            if (leftRow <= 0)
            {
                leftRow = 1;
            }

            if (rightRow <= 0)
            {
                rightRow = MapLeftRowToRight(leftRow);
            }

            SuggestedLeftRow = leftRow;
            SuggestedRightRow = rightRow;
            _viewportSheet = sheet;
            _viewportRow = leftRow;
            _viewportLeftRow = leftRow;
            _viewportRightRow = rightRow;

            UpdateViewportVisuals();
            NavigateMapped?.Invoke(ratio, leftRow, rightRow);
            NavigateRequested?.Invoke(ratio, item);
            e.Handled = true;
        }

        private void MiniMapControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }

            _dragging = true;
            CaptureMouse();
            RaiseNavigate(e.GetPosition(MapBorder));
            e.Handled = true;
        }

        private void MiniMapControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging && e.LeftButton == MouseButtonState.Pressed)
            {
                RaiseNavigate(e.GetPosition(MapBorder));
                e.Handled = true;
            }
        }

        private void MiniMapControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                if (IsMouseCaptured)
                {
                    ReleaseMouseCapture();
                }

                e.Handled = true;
            }
        }

        private void MapBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!e.Handled)
            {
                MiniMapControl_PreviewMouseLeftButtonDown(sender, e);
            }
        }

        private void MapBorder_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!e.Handled)
            {
                MiniMapControl_PreviewMouseMove(sender, e);
            }
        }

        private void MapBorder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!e.Handled)
            {
                MiniMapControl_PreviewMouseLeftButtonUp(sender, e);
            }
        }

        private void MapBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Rebuild();
        }
    }
}
