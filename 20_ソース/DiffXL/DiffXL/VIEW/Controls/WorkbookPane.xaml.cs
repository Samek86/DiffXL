using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DiffXL.COMMON;
using DiffXL.LOGIC.Excel;
using Microsoft.Win32;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// 左右いずれか 1 枚の Excel ブック表示ペイン。
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
        /// Excel 埋め込みホスト。
        /// </summary>
        private readonly ExcelHostControl _host = new ExcelHostControl();

        /// <summary>
        /// 現在のブックセッション。
        /// </summary>
        private ExcelWorkbookSession _session;

        /// <summary>
        /// シート切替中の再入防止。
        /// </summary>
        private bool _suppressSheetEvent;

        /// <summary>
        /// 最後に把握した ScrollRow（ホイール時 Get 失敗のフォールバック）。
        /// </summary>
        private int _lastKnownScrollRow = 1;

        /// <summary>
        /// 最後に把握した ScrollColumn。
        /// </summary>
        private int _lastKnownScrollCol = 1;

        /// <summary>
        /// ホイール等でスクロール操作が成功した直後（verify 後の実 ScrollRow/Col）。
        /// 引数: 自身, row, col, horizontal。
        /// </summary>
        public event Action<WorkbookPane, int, int, bool> ScrollInteracted;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public WorkbookPane()
        {
            InitializeComponent();
            _host.HorizontalAlignment = HorizontalAlignment.Stretch;
            _host.VerticalAlignment = VerticalAlignment.Stretch;
            Panel.SetZIndex(_host, 0);
            HostContainer.Children.Add(_host);
            // GapOverlay は XAML で ZIndex=100・IsHitTestVisible=false
            Unloaded += WorkbookPane_Unloaded;
            SizeChanged += WorkbookPane_SizeChanged;
            HostContainer.SizeChanged += HostContainer_SizeChanged;
            HostContainer.MouseEnter += HostContainer_MouseEnter;
            Focusable = true;
        }

        /// <summary>
        /// マウスが Excel 上に入ったらフォーカスを渡し、クリックなしでホイール可能にする。
        /// </summary>
        private void HostContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_session != null && _session.IsOpen)
            {
                _session.ActivateForInput();
            }
        }

        /// <summary>
        /// ペインサイズ変更時に Excel を追従させる。
        /// </summary>
        private void WorkbookPane_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged || e.HeightChanged)
            {
                _host.ResizeExcelToHost(force: true);
            }
        }

        private void HostContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged || e.HeightChanged)
            {
                _host.ResizeExcelToHost(force: true);
            }
        }

        /// <summary>
        /// 埋め込み Excel を強制リサイズ（親ウィンドウ拡大時用）。
        /// </summary>
        public void ForceResizeHost()
        {
            _host.ResizeExcelToHost(force: true);
        }

        /// <summary>
        /// スクリーン座標が Excel ホスト上にあるか（DPI 対応の PointToScreen 矩形）。
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
        /// ヒット矩形の中心 X（左右どちらに近いか判定用）。
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
        /// ホイール 1 刻み分の縦スクロール（未フォーカスでも可）。
        /// </summary>
        public bool TryScrollByWheelDelta(int wheelDelta)
        {
            return TryScrollByWheelDelta(wheelDelta, horizontal: false);
        }

        /// <summary>
        /// ホイール 1 刻み分スクロールする。
        /// </summary>
        /// <param name="wheelDelta">Windows のホイール delta（通常 ±120）</param>
        /// <param name="horizontal">true なら横（ScrollColumn）</param>
        /// <returns>ペイン上で処理を試みたとき true</returns>
        public bool TryScrollByWheelDelta(int wheelDelta, bool horizontal)
        {
            if (_session == null || !_session.IsOpen || wheelDelta == 0)
            {
                return false;
            }

            _session.ActivateForInput();

            int notches = wheelDelta / 120;
            if (notches == 0)
            {
                notches = wheelDelta > 0 ? 1 : -1;
            }

            // ホイール上 / チルト右 → 行・列番号を減らす／増やす
            int step = -notches * (horizontal ? 2 : 3);

            int curR = _lastKnownScrollRow;
            int curC = _lastKnownScrollCol;
            if (_session.TryGetScroll(out curR, out curC))
            {
                _lastKnownScrollRow = curR;
                _lastKnownScrollCol = curC;
            }

            int nextR = curR;
            int nextC = curC;
            if (horizontal)
            {
                nextC = Math.Max(1, curC + step);
            }
            else
            {
                nextR = Math.Max(1, curR + step);
            }

            bool ok = _session.TrySetScroll(nextR, nextC);
            if (!ok && !horizontal)
            {
                ok = _session.TryGotoRow(nextR);
            }

            // COM が弱いとき: Win32 の横／縦スクロールメッセージ
            if (!ok || horizontal)
            {
                try
                {
                    IntPtr hwnd = _session.GetMainWindowHandle();
                    if (hwnd != IntPtr.Zero)
                    {
                        int code = horizontal
                            ? (step > 0 ? Win32.SB_LINERIGHT : Win32.SB_LINELEFT)
                            : (step > 0 ? Win32.SB_LINEDOWN : Win32.SB_LINEUP);
                        int msg = horizontal ? Win32.WM_HSCROLL : Win32.WM_VSCROLL;
                        int times = Math.Max(1, Math.Abs(notches) * (horizontal ? 2 : 3));
                        for (int i = 0; i < times; i++)
                        {
                            Win32.SendMessage(hwnd, msg, (IntPtr)code, IntPtr.Zero);
                        }

                        ok = true;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // ネイティブ転送（横は HWHEEL、縦は WHEEL）
            try
            {
                Win32.POINT pt;
                if (Win32.GetCursorPos(out pt))
                {
                    if (horizontal)
                    {
                        _host.ForwardMouseHWheel(wheelDelta, pt.X, pt.Y);
                    }
                    else
                    {
                        _host.ForwardMouseWheel(wheelDelta, pt.X, pt.Y);
                    }
                }
            }
            catch
            {
                // ignore
            }

            _lastKnownScrollRow = nextR;
            _lastKnownScrollCol = nextC;
            int verifyR, verifyC;
            if (_session.TryGetScroll(out verifyR, out verifyC))
            {
                _lastKnownScrollRow = verifyR;
                _lastKnownScrollCol = verifyC;
            }

            // イベント駆動同期: 同一 UI スレッドで相手側へ即 Apply
            try
            {
                ScrollInteracted?.Invoke(this, _lastKnownScrollRow, _lastKnownScrollCol, horizontal);
            }
            catch
            {
                // ignore subscriber errors
            }

            return true;
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
        /// 画面ピクセル移動量から縦横パンする（中ボタンドラッグ等）。
        /// dy&gt;0 = 下へドラッグ = 内容を下へ（行番号減）、Excel 的には「掴んで動かす」。
        /// </summary>
        public bool TryPanByPixels(double dx, double dy)
        {
            if (_session == null || !_session.IsOpen)
            {
                return false;
            }

            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
            {
                return true;
            }

            _session.ActivateForInput();

            int curR = _lastKnownScrollRow;
            int curC = _lastKnownScrollCol;
            if (_session.TryGetScroll(out curR, out curC))
            {
                _lastKnownScrollRow = curR;
                _lastKnownScrollCol = curC;
            }

            // 感度: 縦 12px ≒ 1 行、横 16px ≒ 1 列（ドラッグで横も効きやすく）
            int dRow = (int)Math.Round(-dy / 12.0);
            int dCol = (int)Math.Round(-dx / 16.0);
            if (dRow == 0 && Math.Abs(dy) >= 3)
            {
                dRow = dy > 0 ? -1 : 1;
            }

            if (dCol == 0 && Math.Abs(dx) >= 3)
            {
                dCol = dx > 0 ? -1 : 1;
            }

            int nextR = Math.Max(1, curR + dRow);
            int nextC = Math.Max(1, curC + dCol);
            bool ok = _session.TrySetScroll(nextR, nextC);
            if (ok)
            {
                _lastKnownScrollRow = nextR;
                _lastKnownScrollCol = nextC;
                int vr, vc;
                if (_session.TryGetScroll(out vr, out vc))
                {
                    _lastKnownScrollRow = vr;
                    _lastKnownScrollCol = vc;
                }
            }

            return true;
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
        /// ブックが開いているか。
        /// </summary>
        public bool IsOpen
        {
            get { return _session != null && _session.IsOpen; }
        }

        /// <summary>
        /// 開いているファイルパス。
        /// </summary>
        public string FilePath
        {
            get { return _session != null ? _session.FilePath : null; }
        }

        /// <summary>
        /// 内部セッション（同期スクロール等で利用）。
        /// </summary>
        public ExcelWorkbookSession Session
        {
            get { return _session; }
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
        /// オープン失敗時イベント。
        /// </summary>
        public event Action<string> OpenFailed;

        /// <summary>
        /// オープン成功時イベント。
        /// </summary>
        public event Action OpenSucceeded;

        /// <summary>
        /// ユーザー操作でシートが切り替わった（プログラムからの TrySelectSheet では発火しない）。
        /// </summary>
        public event Action<string> SheetChangedByUser;

        /// <summary>
        /// ファイルを読み取り専用で開いて埋め込む。
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

            if (!ExcelAvailability.IsExcelInstalled())
            {
                RaiseOpenFailed(ExcelAvailability.GetDiagnosticMessage());
                return;
            }

            try
            {
                CloseFile();

                var session = new ExcelWorkbookSession();
                session.OpenReadOnly(fullPath);
                IntPtr hwnd = session.GetMainWindowHandle();
                if (hwnd == IntPtr.Zero)
                {
                    session.Dispose();
                    RaiseOpenFailed("Excel ウィンドウの埋め込みに失敗しました。ログを確認してください。");
                    return;
                }

                // レイアウト確定後に Attach（ホスト HWND が必要なため）
                _session = session;
                PathText.Text = fullPath;
                PathText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                AttachWhenReady(hwnd);
                LoadSheets();
                OpenSucceeded?.Invoke();
                Log.Info(PaneTitle + " にファイルを表示: " + fullPath);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                CloseFile();
                RaiseOpenFailed(ex.Message);
            }
        }

        /// <summary>
        /// ファイルを閉じて埋め込みを解除する。
        /// </summary>
        public void CloseFile()
        {
            try
            {
                _host.Detach();
            }
            catch (Exception ex)
            {
                Log.Debug("Host.Detach: " + ex.Message);
            }

            if (_session != null)
            {
                try
                {
                    _session.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Debug("Session.Dispose: " + ex.Message);
                }

                _session = null;
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
        /// 表示メトリクスを取得する。
        /// </summary>
        /// <param name="metrics">結果</param>
        /// <returns>成功時 true</returns>
        public bool TryGetViewMetrics(out ExcelViewMetrics metrics)
        {
            metrics = null;
            if (_session == null)
            {
                return false;
            }

            return _session.TryGetViewMetrics(out metrics);
        }

        /// <summary>
        /// ホスト HWND 生成後に Attach する。
        /// </summary>
        /// <param name="hwnd">Excel HWND</param>
        private void AttachWhenReady(IntPtr hwnd)
        {
            Action attach = () =>
            {
                try
                {
                    if (_session == null || !_session.IsOpen)
                    {
                        return;
                    }

                    _host.Attach(hwnd);
                    if (_session != null)
                    {
                        _session.EnsureViewerChrome();
                    }

                    _host.ResizeExcelToHost(force: true);
                    // 埋め込み直後はリボン再表示されることがあるので遅延でもう一度
                    _host.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                    {
                        try
                        {
                            if (_session != null)
                            {
                                _session.EnsureViewerChrome();
                            }

                            _host.ResizeExcelToHost(force: true);
                        }
                        catch
                        {
                            // ignore
                        }
                    }));
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                    RaiseOpenFailed("Excel ウィンドウの埋め込みに失敗しました: " + ex.Message);
                }
            };

            // HwndHost の BuildWindowCore 後に実行する
            _host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, attach);
        }

        /// <summary>
        /// シート一覧を ComboBox に載せる。
        /// </summary>
        private void LoadSheets()
        {
            _suppressSheetEvent = true;
            try
            {
                SheetCombo.Items.Clear();
                if (_session == null || !_session.IsOpen)
                {
                    SheetCombo.IsEnabled = false;
                    return;
                }

                foreach (string name in _session.GetSheetNames())
                {
                    SheetCombo.Items.Add(name);
                }

                SheetCombo.IsEnabled = SheetCombo.Items.Count > 0;
                if (SheetCombo.Items.Count > 0)
                {
                    SheetCombo.SelectedIndex = 0;
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
        /// シートをアクティブ化し、ComboBox 表示も合わせる（MiniMap ジャンプ用）。
        /// </summary>
        /// <param name="sheetName">シート名</param>
        /// <returns>成功時 true</returns>
        public bool TrySelectSheet(string sheetName)
        {
            if (_session == null || !_session.IsOpen || string.IsNullOrWhiteSpace(sheetName))
            {
                return false;
            }

            try
            {
                _session.ActivateSheet(sheetName);
                _suppressSheetEvent = true;
                try
                {
                    for (int i = 0; i < SheetCombo.Items.Count; i++)
                    {
                        string name = SheetCombo.Items[i] as string;
                        if (string.Equals(name, sheetName, StringComparison.OrdinalIgnoreCase))
                        {
                            SheetCombo.SelectedIndex = i;
                            break;
                        }
                    }
                }
                finally
                {
                    _suppressSheetEvent = false;
                }

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
            if (_suppressSheetEvent || _session == null || !_session.IsOpen)
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
                _session.ActivateSheet(name);
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
        /// アンロード時に Excel を閉じる。
        /// </summary>
        private void WorkbookPane_Unloaded(object sender, RoutedEventArgs e)
        {
            CloseFile();
        }

        /// <summary>
        /// 失敗イベントを発火する。
        /// </summary>
        /// <param name="message">メッセージ</param>
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
