using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;
using DiffXL.LOGIC.Excel;
using Microsoft.Win32;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// 左右いずれか 1 枚のブック表示ペイン（内容ベース ContentPane ホスト）。
    /// Excel COM 埋め込みは行わない。
    /// </summary>
    public partial class WorkbookPane : UserControl
    {
        /// <summary>
        /// ペインタイトル依存プロパティ。
        /// </summary>
        public static readonly DependencyProperty PaneTitleProperty = DependencyProperty.Register(
            nameof(PaneTitle),
            typeof(string),
            typeof(WorkbookPane),
            new PropertyMetadata("ブック"));

        /// <summary>
        /// 開いているファイルパス。
        /// </summary>
        private string _filePath;

        /// <summary>
        /// 比較結果のブック内容（シート切替用）。
        /// </summary>
        private WorkbookContent _workbook;

        /// <summary>
        /// 相手側ブック内容（テーブル行アライン用）。
        /// </summary>
        private WorkbookContent _partnerWorkbook;

        /// <summary>
        /// 相手側の優先シート名（シート対応ペア）。
        /// </summary>
        private string _partnerPreferredSheetName;

        /// <summary>
        /// 比較結果の全差分。
        /// </summary>
        private IList<DiffItem> _allDiffs = new List<DiffItem>();

        /// <summary>
        /// 左ペインかどうか。
        /// </summary>
        private bool _isLeft = true;

        /// <summary>
        /// シート切替中の再入防止。
        /// </summary>
        private bool _suppressSheetEvent;

        /// <summary>
        /// 最後に把握した ScrollRow（互換用キャッシュ。内容ビューでは未使用）。
        /// </summary>
        private int _lastKnownScrollRow = 1;

        /// <summary>
        /// 最後に把握した ScrollColumn。
        /// </summary>
        private int _lastKnownScrollCol = 1;

        /// <summary>
        /// ホイール等でスクロール操作が成功した直後（互換イベント・内容ビューでは未使用）。
        /// 引数: 自身, row, col, horizontal。
        /// </summary>
#pragma warning disable CS0067
        public event Action<WorkbookPane, int, int, bool> ScrollInteracted;
#pragma warning restore CS0067

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public WorkbookPane()
        {
            InitializeComponent();
            Unloaded += WorkbookPane_Unloaded;
            Focusable = true;
        }

        /// <summary>
        /// 埋め込み Excel を強制リサイズ（互換 no-op。内容ビューはレイアウト自動追従）。
        /// </summary>
        public void ForceResizeHost()
        {
            // ContentPane は WPF レイアウトに従うため不要
        }

        /// <summary>
        /// スクリーン座標がホスト上にあるか。
        /// </summary>
        public bool ContainsScreenPoint(Point screenPoint)
        {
            FrameworkElement target = HostContainer != null && HostContainer.IsVisible && HostContainer.ActualWidth > 2
                ? (FrameworkElement)HostContainer
                : this;
            if (target == null || !target.IsVisible || target.ActualWidth < 2 || target.ActualHeight < 2)
            {
                return false;
            }

            try
            {
                Point tl = target.PointToScreen(new Point(0, 0));
                Point br = target.PointToScreen(new Point(target.ActualWidth, target.ActualHeight));
                double left = Math.Min(tl.X, br.X) - 2;
                double right = Math.Max(tl.X, br.X) + 2;
                double top = Math.Min(tl.Y, br.Y) - 2;
                double bottom = Math.Max(tl.Y, br.Y) + 2;
                return screenPoint.X >= left
                    && screenPoint.X <= right
                    && screenPoint.Y >= top
                    && screenPoint.Y <= bottom;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ヒット矩形の中心 X。
        /// </summary>
        public double GetScreenCenterX()
        {
            try
            {
                FrameworkElement target = HostContainer != null ? (FrameworkElement)HostContainer : this;
                Point tl = target.PointToScreen(new Point(0, 0));
                Point br = target.PointToScreen(new Point(target.ActualWidth, target.ActualHeight));
                return (tl.X + br.X) / 2.0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// ホイール 1 刻み分の縦スクロール（内容ビューでは未対応）。
        /// </summary>
        public bool TryScrollByWheelDelta(int wheelDelta)
        {
            return TryScrollByWheelDelta(wheelDelta, horizontal: false);
        }

        /// <summary>
        /// ホイール 1 刻み分スクロールする（内容ビューでは未対応）。
        /// </summary>
        public bool TryScrollByWheelDelta(int wheelDelta, bool horizontal)
        {
            return false;
        }

        /// <summary>
        /// 現在の ScrollRow（キャッシュ込み）。
        /// </summary>
        public int LastKnownScrollRow
        {
            get { return _lastKnownScrollRow; }
        }

        /// <summary>
        /// 現在の ScrollColumn（キャッシュ込み）。
        /// </summary>
        public int LastKnownScrollCol
        {
            get { return _lastKnownScrollCol; }
        }

        /// <summary>
        /// スクロール位置キャッシュを更新する。
        /// </summary>
        public void NoteScrollRow(int row)
        {
            if (row > 0)
            {
                _lastKnownScrollRow = row;
            }
        }

        /// <summary>
        /// スクロール位置キャッシュを更新する。
        /// </summary>
        public void NoteScroll(int row, int col)
        {
            if (row > 0)
            {
                _lastKnownScrollRow = row;
            }

            if (col > 0)
            {
                _lastKnownScrollCol = col;
            }
        }

        /// <summary>
        /// 画面ピクセル移動量からパンする（内容ビューでは未対応）。
        /// </summary>
        public bool TryPanByPixels(double dx, double dy)
        {
            return false;
        }

        /// <summary>
        /// ペイン見出し（左 / 右 など）。
        /// </summary>
        public string PaneTitle
        {
            get { return (string)GetValue(PaneTitleProperty); }
            set { SetValue(PaneTitleProperty, value); }
        }

        /// <summary>
        /// ブックが開いているか（パス設定済み）。
        /// </summary>
        public bool IsOpen
        {
            get { return !string.IsNullOrEmpty(_filePath) && File.Exists(_filePath); }
        }

        /// <summary>
        /// 開いているファイルパス。
        /// </summary>
        public string FilePath
        {
            get { return _filePath; }
        }

        /// <summary>
        /// 内部セッション（Excel 廃止のため常に null。互換 API）。
        /// </summary>
        public ExcelWorkbookSession Session
        {
            get { return null; }
        }

        /// <summary>
        /// 内容ホスト。
        /// </summary>
        public ContentPane ContentHostControl
        {
            get { return ContentHost; }
        }

        /// <summary>
        /// 保持中のブック内容。
        /// </summary>
        public WorkbookContent Workbook
        {
            get { return _workbook; }
        }

        /// <summary>
        /// ComboBox 上で選択中のシート名。
        /// </summary>
        public string SelectedSheetName
        {
            get
            {
                return SheetCombo != null && SheetCombo.SelectedItem != null
                    ? SheetCombo.SelectedItem.ToString()
                    : null;
            }
        }

        /// <summary>
        /// シート名一覧（内容モデルから）。
        /// </summary>
        public IReadOnlyList<string> GetSheetNames()
        {
            if (_workbook == null || _workbook.Sheets == null)
            {
                return Array.Empty<string>();
            }

            return _workbook.Sheets
                .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                .Select(s => s.Name)
                .ToList();
        }

        /// <summary>
        /// オープン失敗時イベント。
        /// </summary>
        public event Action<string> OpenFailed;

        /// <summary>
        /// オープン成功時イベント。
        /// </summary>
        public event Action OpenSucceeded;

        /// <summary>
        /// ユーザー操作でシートが切り替わった。
        /// </summary>
        public event Action<string> SheetChangedByUser;

        /// <summary>
        /// ファイルパスを設定する（Excel は起動しない。.xlsx の存在確認のみ）。
        /// </summary>
        /// <param name="path">xlsx パス</param>
        public void OpenFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                RaiseOpenFailed("ファイルパスが指定されていません。");
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                RaiseOpenFailed("パスが不正です: " + ex.Message);
                return;
            }

            if (!string.Equals(Path.GetExtension(fullPath), Common.ExcelExtension, StringComparison.OrdinalIgnoreCase))
            {
                RaiseOpenFailed("対象形式は .xlsx のみです。");
                return;
            }

            if (!File.Exists(fullPath))
            {
                RaiseOpenFailed("ファイルが見つかりません: " + fullPath);
                return;
            }

            try
            {
                CloseFile();
                _filePath = fullPath;
                PathText.Text = fullPath;
                PathText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                OpenSucceeded?.Invoke();
                Log.Info(PaneTitle + " にファイルを設定（内容ビュー）: " + fullPath);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                CloseFile();
                RaiseOpenFailed(ex.Message);
            }
        }

        /// <summary>
        /// 比較結果のブック内容を読み込み、指定シートを ContentPane に表示する。
        /// </summary>
        /// <param name="workbook">ブック内容</param>
        /// <param name="allDiffs">全差分</param>
        /// <param name="isLeft">左ペインなら true</param>
        /// <param name="preferredSheetName">表示したいシート名（null なら先頭）</param>
        public void LoadWorkbookContent(
            WorkbookContent workbook,
            IList<DiffItem> allDiffs,
            bool isLeft,
            string preferredSheetName = null)
        {
            LoadWorkbookContent(
                workbook,
                allDiffs,
                isLeft,
                preferredSheetName,
                partnerWorkbook: null,
                partnerPreferredSheetName: null);
        }

        /// <summary>
        /// 比較結果のブック内容と相手ブックを読み込み、指定シートを ContentPane に表示する。
        /// </summary>
        /// <param name="workbook">ブック内容</param>
        /// <param name="allDiffs">全差分</param>
        /// <param name="isLeft">左ペインなら true</param>
        /// <param name="preferredSheetName">表示したいシート名（null なら先頭）</param>
        /// <param name="partnerWorkbook">相手側ブック（テーブルアライン用）</param>
        /// <param name="partnerPreferredSheetName">相手側シート名</param>
        public void LoadWorkbookContent(
            WorkbookContent workbook,
            IList<DiffItem> allDiffs,
            bool isLeft,
            string preferredSheetName,
            WorkbookContent partnerWorkbook,
            string partnerPreferredSheetName)
        {
            _workbook = workbook;
            _partnerWorkbook = partnerWorkbook;
            _partnerPreferredSheetName = partnerPreferredSheetName;
            _allDiffs = allDiffs ?? new List<DiffItem>();
            _isLeft = isLeft;

            if (workbook != null && !string.IsNullOrEmpty(workbook.Path))
            {
                _filePath = workbook.Path;
                PathText.Text = workbook.Path;
                PathText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            }

            PopulateSheetCombo(preferredSheetName);
            string sheetName = SelectedSheetName ?? preferredSheetName;
            ShowSheet(sheetName);
        }

        /// <summary>
        /// 単一シートを ContentPane に読み込む。
        /// </summary>
        public void LoadContent(SheetContent sheet, IList<DiffItem> sheetDiffs, bool isLeft)
        {
            _isLeft = isLeft;
            if (ContentHost != null)
            {
                ContentHost.Load(sheet, sheetDiffs, isLeft, partnerSheet: null);
            }
        }

        /// <summary>
        /// ファイル表示をクリアする。
        /// </summary>
        public void CloseFile()
        {
            _filePath = null;
            _workbook = null;
            _partnerWorkbook = null;
            _partnerPreferredSheetName = null;
            _allDiffs = new List<DiffItem>();
            if (ContentHost != null)
            {
                ContentHost.Load(null, null, _isLeft, partnerSheet: null);
            }

            PathText.Text = "（未選択）";
            PathText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            _suppressSheetEvent = true;
            try
            {
                SheetCombo.Items.Clear();
                SheetCombo.IsEnabled = false;
            }
            finally
            {
                _suppressSheetEvent = false;
            }
        }

        /// <summary>
        /// 表示メトリクスを取得する（Excel 廃止のため常に false）。
        /// </summary>
        public bool TryGetViewMetrics(out ExcelViewMetrics metrics)
        {
            metrics = null;
            return false;
        }

        /// <summary>
        /// シート一覧を ComboBox に載せる。
        /// </summary>
        private void PopulateSheetCombo(string preferredSheetName)
        {
            _suppressSheetEvent = true;
            try
            {
                SheetCombo.Items.Clear();
                if (_workbook == null || _workbook.Sheets == null || _workbook.Sheets.Count == 0)
                {
                    SheetCombo.IsEnabled = false;
                    return;
                }

                foreach (SheetContent sheet in _workbook.Sheets)
                {
                    if (sheet != null && !string.IsNullOrEmpty(sheet.Name))
                    {
                        SheetCombo.Items.Add(sheet.Name);
                    }
                }

                SheetCombo.IsEnabled = SheetCombo.Items.Count > 0;
                int index = 0;
                if (!string.IsNullOrEmpty(preferredSheetName))
                {
                    for (int i = 0; i < SheetCombo.Items.Count; i++)
                    {
                        if (string.Equals(
                            SheetCombo.Items[i] as string,
                            preferredSheetName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }

                if (SheetCombo.Items.Count > 0)
                {
                    SheetCombo.SelectedIndex = index;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                SheetCombo.IsEnabled = false;
            }
            finally
            {
                _suppressSheetEvent = false;
            }
        }

        /// <summary>
        /// 指定シートを ContentPane に表示する。
        /// </summary>
        private void ShowSheet(string sheetName)
        {
            SheetContent sheet = FindSheet(_workbook, sheetName);
            SheetContent partner = ResolvePartnerSheet(sheet);
            IList<DiffItem> sheetDiffs = FilterDiffsForSheet(sheet);
            if (ContentHost != null)
            {
                ContentHost.Load(sheet, sheetDiffs, _isLeft, partner);
            }
        }

        /// <summary>
        /// 相手側シートを解決する（優先名 → 同名 → 先頭）。
        /// </summary>
        private SheetContent ResolvePartnerSheet(SheetContent selfSheet)
        {
            if (_partnerWorkbook == null || _partnerWorkbook.Sheets == null)
            {
                return null;
            }

            SheetContent byPreferred = FindSheet(_partnerWorkbook, _partnerPreferredSheetName);
            if (byPreferred != null)
            {
                return byPreferred;
            }

            if (selfSheet != null && !string.IsNullOrEmpty(selfSheet.Name))
            {
                SheetContent sameName = FindSheet(_partnerWorkbook, selfSheet.Name);
                if (sameName != null)
                {
                    return sameName;
                }
            }

            return _partnerWorkbook.Sheets.FirstOrDefault(s => s != null);
        }

        /// <summary>
        /// シート名で SheetContent を探す。
        /// </summary>
        private SheetContent FindSheet(string sheetName)
        {
            return FindSheet(_workbook, sheetName);
        }

        /// <summary>
        /// 指定ブックからシート名で SheetContent を探す。
        /// </summary>
        private static SheetContent FindSheet(WorkbookContent workbook, string sheetName)
        {
            if (workbook == null || workbook.Sheets == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(sheetName))
            {
                foreach (SheetContent s in workbook.Sheets)
                {
                    if (s != null && string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return s;
                    }
                }
            }

            return workbook.Sheets.FirstOrDefault(s => s != null);
        }

        /// <summary>
        /// シートに関連する差分を抽出する。
        /// </summary>
        private IList<DiffItem> FilterDiffsForSheet(SheetContent sheet)
        {
            if (_allDiffs == null || _allDiffs.Count == 0)
            {
                return new List<DiffItem>();
            }

            string name = sheet != null ? sheet.Name : null;
            if (string.IsNullOrEmpty(name))
            {
                return new List<DiffItem>();
            }

            var list = new List<DiffItem>();
            foreach (DiffItem d in _allDiffs)
            {
                if (d == null)
                {
                    continue;
                }

                bool match = _isLeft
                    ? string.Equals(d.SheetLeft, name, StringComparison.OrdinalIgnoreCase)
                      || (string.IsNullOrEmpty(d.SheetLeft)
                          && string.Equals(d.SheetRight, name, StringComparison.OrdinalIgnoreCase))
                    : string.Equals(d.SheetRight, name, StringComparison.OrdinalIgnoreCase)
                      || (string.IsNullOrEmpty(d.SheetRight)
                          && string.Equals(d.SheetLeft, name, StringComparison.OrdinalIgnoreCase));
                if (match)
                {
                    list.Add(d);
                }
            }

            return list;
        }

        /// <summary>
        /// 開くボタン。
        /// </summary>
        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Excel ブック (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                CheckFileExists = true,
                Multiselect = false,
                Title = PaneTitle + " の Excel ファイルを選択"
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                OpenFile(dialog.FileName);
            }
        }

        /// <summary>
        /// シートを選択し ContentPane を更新する。
        /// </summary>
        /// <param name="sheetName">シート名</param>
        /// <returns>成功時 true</returns>
        public bool TrySelectSheet(string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName))
            {
                return false;
            }

            try
            {
                _suppressSheetEvent = true;
                try
                {
                    bool found = false;
                    for (int i = 0; i < SheetCombo.Items.Count; i++)
                    {
                        string name = SheetCombo.Items[i] as string;
                        if (string.Equals(name, sheetName, StringComparison.OrdinalIgnoreCase))
                        {
                            SheetCombo.SelectedIndex = i;
                            found = true;
                            break;
                        }
                    }

                    if (!found && _workbook != null)
                    {
                        // コンボに無いが内容にある場合は追加
                        SheetContent s = FindSheet(sheetName);
                        if (s != null)
                        {
                            SheetCombo.Items.Add(s.Name);
                            SheetCombo.SelectedItem = s.Name;
                            SheetCombo.IsEnabled = true;
                            found = true;
                        }
                    }

                    if (!found)
                    {
                        return false;
                    }
                }
                finally
                {
                    _suppressSheetEvent = false;
                }

                ShowSheet(sheetName);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(PaneTitle + " TrySelectSheet 失敗: " + sheetName + " / " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// シート選択変更。
        /// </summary>
        private void SheetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSheetEvent)
            {
                return;
            }

            string name = SheetCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            try
            {
                ShowSheet(name);
                SheetChangedByUser?.Invoke(name);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                MessageBox.Show(
                    ex.Message,
                    Common.AppDisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// アンロード時にクリアする。
        /// </summary>
        private void WorkbookPane_Unloaded(object sender, RoutedEventArgs e)
        {
            CloseFile();
        }

        /// <summary>
        /// 失敗イベントを発火する。
        /// </summary>
        private void RaiseOpenFailed(string message)
        {
            Log.Error(PaneTitle + " OpenFailed: " + message);
            if (OpenFailed != null)
            {
                OpenFailed(message);
            }
            else
            {
                MessageBox.Show(
                    message,
                    Common.AppDisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
