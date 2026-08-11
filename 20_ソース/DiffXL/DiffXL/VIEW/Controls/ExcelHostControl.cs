using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DiffXL.COMMON;
using DiffXL.LOGIC.Excel;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// Excel ウィンドウを WPF 内に埋め込み、親サイズに常に追従させる。
    /// </summary>
    public sealed class ExcelHostControl : HwndHost
    {
        private const int WM_SIZE = 0x0005;
        private const int SIZE_RESTORED = 0;

        private IntPtr _hwndHost = IntPtr.Zero;
        private IntPtr _excelHwnd = IntPtr.Zero;
        private int _originalExcelStyle;
        private bool _styleSaved;
        private Size _lastPixelSize;

        public ExcelHostControl()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            Focusable = true;
            SnapsToDevicePixels = true;
            // LayoutUpdated でのリサイズは無限ループ（StackOverflow）の原因になるため使わない
        }

        public bool IsAttached
        {
            get { return _excelHwnd != IntPtr.Zero && Win32.IsWindow(_excelHwnd); }
        }

        /// <summary>
        /// Excel の HWND をホストに埋め込む。
        /// </summary>
        public void Attach(IntPtr excelHwnd)
        {
            if (excelHwnd == IntPtr.Zero)
            {
                throw new ArgumentException("無効なウィンドウハンドルです。", nameof(excelHwnd));
            }

            EnsureHostHandle();
            if (_hwndHost == IntPtr.Zero)
            {
                throw new InvalidOperationException("ホストウィンドウがまだ初期化されていません。");
            }

            Detach();

            _excelHwnd = excelHwnd;
            _originalExcelStyle = Win32.GetWindowLong(excelHwnd, Win32.GWL_STYLE);
            _styleSaved = true;

            // 最大化・枠付きのまま埋め込むと親サイズに追従しない
            int style = _originalExcelStyle;
            style |= Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPSIBLINGS | Win32.WS_CLIPCHILDREN;
            style &= ~Win32.WS_POPUP;
            style &= ~Win32.WS_CAPTION;
            style &= ~Win32.WS_THICKFRAME;
            style &= ~Win32.WS_BORDER;
            style &= ~Win32.WS_SYSMENU;
            style &= ~Win32.WS_MINIMIZEBOX;
            style &= ~Win32.WS_MAXIMIZEBOX;
            style &= ~Win32.WS_MAXIMIZE;
            style &= ~Win32.WS_MINIMIZE;
            Win32.SetWindowLong(excelHwnd, Win32.GWL_STYLE, style);

            int ex = Win32.GetWindowLong(excelHwnd, Win32.GWL_EXSTYLE);
            ex &= ~(Win32.WS_EX_DLGMODALFRAME | Win32.WS_EX_CLIENTEDGE | Win32.WS_EX_STATICEDGE | Win32.WS_EX_WINDOWEDGE);
            Win32.SetWindowLong(excelHwnd, Win32.GWL_EXSTYLE, ex);

            // 最大化状態を解除してから親子付け
            Win32.ShowWindow(excelHwnd, Win32.SW_RESTORE);
            Win32.SetParent(excelHwnd, _hwndHost);
            Win32.ShowWindow(excelHwnd, Win32.SW_SHOW);

            // スタイル変更を反映
            Win32.SetWindowPos(
                excelHwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);

            _lastPixelSize = new Size(0, 0);
            ResizeExcelToHost(force: true);
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => ResizeExcelToHost(force: true)));
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(() => ResizeExcelToHost(force: true)));
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => ResizeExcelToHost(force: true)));
            Log.Debug("Excel をホストへアタッチ: " + excelHwnd);
        }

        public void Detach()
        {
            if (_excelHwnd == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (Win32.IsWindow(_excelHwnd))
                {
                    Win32.SetParent(_excelHwnd, IntPtr.Zero);
                    if (_styleSaved)
                    {
                        Win32.SetWindowLong(_excelHwnd, Win32.GWL_STYLE, _originalExcelStyle);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Excel Detach 失敗: " + ex.Message);
            }
            finally
            {
                _excelHwnd = IntPtr.Zero;
                _styleSaved = false;
                _lastPixelSize = new Size(0, 0);
            }
        }

        /// <summary>
        /// ホストと Excel を現在のコントロールサイズに合わせる。
        /// </summary>
        public void ResizeExcelToHost()
        {
            ResizeExcelToHost(force: false);
        }

        /// <summary>
        /// ホストと Excel を現在のコントロールサイズに合わせる。
        /// </summary>
        /// <param name="force">同一サイズでも再適用する</param>
        public void ResizeExcelToHost(bool force)
        {
            if (_hwndHost == IntPtr.Zero || !Win32.IsWindow(_hwndHost))
            {
                return;
            }

            int width;
            int height;
            GetPixelSize(out width, out height);
            if (width < 2 || height < 2)
            {
                return;
            }

            bool same = !force
                        && Math.Abs(_lastPixelSize.Width - width) < 1
                        && Math.Abs(_lastPixelSize.Height - height) < 1
                        && _lastPixelSize.Width > 0;
            if (same)
            {
                return;
            }

            _lastPixelSize = new Size(width, height);

            // ホスト HWND
            Win32.SetWindowPos(
                _hwndHost,
                IntPtr.Zero,
                0,
                0,
                width,
                height,
                Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);

            if (_excelHwnd != IntPtr.Zero && Win32.IsWindow(_excelHwnd))
            {
                // 最大化フラグが復活していないか毎回落とす
                int style = Win32.GetWindowLong(_excelHwnd, Win32.GWL_STYLE);
                if ((style & Win32.WS_MAXIMIZE) != 0 || (style & Win32.WS_POPUP) != 0)
                {
                    style |= Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPSIBLINGS | Win32.WS_CLIPCHILDREN;
                    style &= ~(Win32.WS_MAXIMIZE | Win32.WS_MINIMIZE | Win32.WS_POPUP
                        | Win32.WS_CAPTION | Win32.WS_THICKFRAME | Win32.WS_BORDER);
                    Win32.SetWindowLong(_excelHwnd, Win32.GWL_STYLE, style);
                }

                Win32.SetWindowPos(
                    _excelHwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    width,
                    height,
                    Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);

                IntPtr lParam = (IntPtr)((height << 16) | (width & 0xFFFF));
                Win32.SendMessage(_excelHwnd, WM_SIZE, (IntPtr)SIZE_RESTORED, lParam);
                Win32.InvalidateRect(_excelHwnd, IntPtr.Zero, true);

                // 主要子ウィンドウもホストいっぱいへ（黒帯対策）
                ResizeExcelChildren(_excelHwnd, width, height);
            }
        }

        /// <summary>
        /// Excel のデスクトップ領域など主要子を親サイズに合わせる。
        /// </summary>
        private static void ResizeExcelChildren(IntPtr excelHwnd, int width, int height)
        {
            try
            {
                // XLDESK がワークシート領域
                IntPtr desk = Win32.FindWindowEx(excelHwnd, IntPtr.Zero, "XLDESK", null);
                if (desk != IntPtr.Zero)
                {
                    Win32.MoveWindow(desk, 0, 0, width, height, true);
                    IntPtr excel7 = Win32.FindWindowEx(desk, IntPtr.Zero, "EXCEL7", null);
                    if (excel7 != IntPtr.Zero)
                    {
                        Win32.MoveWindow(excel7, 0, 0, width, height, true);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// 縦ホイールを埋め込み Excel へ転送する。
        /// </summary>
        public bool ForwardMouseWheel(int delta, int screenX, int screenY)
        {
            return ForwardMouseWheelCore(Win32.WM_MOUSEWHEEL, delta, screenX, screenY);
        }

        /// <summary>
        /// 横ホイール（チルト／Shift+ホイール相当）を埋め込み Excel へ転送する。
        /// </summary>
        public bool ForwardMouseHWheel(int delta, int screenX, int screenY)
        {
            return ForwardMouseWheelCore(Win32.WM_MOUSEHWHEEL, delta, screenX, screenY);
        }

        private bool ForwardMouseWheelCore(int message, int delta, int screenX, int screenY)
        {
            if (_excelHwnd == IntPtr.Zero || !Win32.IsWindow(_excelHwnd))
            {
                return false;
            }

            IntPtr target = Win32.WindowFromPoint(new Win32.POINT { X = screenX, Y = screenY });
            if (target == IntPtr.Zero)
            {
                target = FindScrollableChild(_excelHwnd);
            }

            if (target == IntPtr.Zero)
            {
                target = _excelHwnd;
            }

            try { Win32.SetFocus(target); } catch { /* ignore */ }

            IntPtr wParam = (IntPtr)((delta << 16) & unchecked((int)0xFFFF0000));
            IntPtr lParam = (IntPtr)((screenY << 16) | (screenX & 0xFFFF));
            Win32.SendMessage(target, message, wParam, lParam);
            return true;
        }

        private static IntPtr FindScrollableChild(IntPtr root)
        {
            IntPtr found = IntPtr.Zero;
            Win32.EnumChildWindows(root, (h, lp) =>
            {
                var sb = new StringBuilder(64);
                Win32.GetClassName(h, sb, sb.Capacity);
                string cls = sb.ToString();
                // グリッド／シート表示
                if (cls.IndexOf("EXCEL7", StringComparison.OrdinalIgnoreCase) >= 0
                    || cls.IndexOf("XLDESK", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = h;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return found;
        }

        protected override Size MeasureOverride(Size constraint)
        {
            double w = constraint.Width;
            double h = constraint.Height;
            if (double.IsInfinity(w) || double.IsNaN(w) || w < 1)
            {
                w = Math.Max(1, ActualWidth > 1 ? ActualWidth : 400);
            }

            if (double.IsInfinity(h) || double.IsNaN(h) || h < 1)
            {
                h = Math.Max(1, ActualHeight > 1 ? ActualHeight : 300);
            }

            return new Size(w, h);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_hwndHost != IntPtr.Zero && finalSize.Width > 1 && finalSize.Height > 1)
            {
                // サイズが変わったときだけ（毎 Arrange の force は StackOverflow の温床）
                ResizeExcelToHost(force: false);
            }

            return finalSize;
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            int width = 200;
            int height = 200;
            GetPixelSize(out width, out height);

            int style = Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN | Win32.WS_CLIPSIBLINGS;
            _hwndHost = Win32.CreateWindowEx(
                0,
                "static",
                string.Empty,
                style,
                0,
                0,
                width,
                height,
                hwndParent.Handle,
                IntPtr.Zero,
                Win32.GetModuleHandle(null),
                IntPtr.Zero);

            if (_hwndHost == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Excel ホストウィンドウの作成に失敗しました。LastError=" + Marshal.GetLastWin32Error());
            }

            return new HandleRef(this, _hwndHost);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Detach();
            if (hwnd.Handle != IntPtr.Zero)
            {
                Win32.DestroyWindow(hwnd.Handle);
            }

            _hwndHost = IntPtr.Zero;
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            if (rcBoundingBox.Width > 1 && rcBoundingBox.Height > 1)
            {
                int width;
                int height;
                ToDevicePixels(rcBoundingBox.Width, rcBoundingBox.Height, out width, out height);
                if (_hwndHost != IntPtr.Zero)
                {
                    Win32.SetWindowPos(
                        _hwndHost,
                        IntPtr.Zero,
                        0,
                        0,
                        width,
                        height,
                        Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
                }

                // 強制適用のためキャッシュを捨てる
                _lastPixelSize = new Size(0, 0);
                ResizeExcelToHost(force: true);
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (sizeInfo.WidthChanged || sizeInfo.HeightChanged)
            {
                ResizeExcelToHost(force: true);
            }
        }

        private void EnsureHostHandle()
        {
            if (_hwndHost != IntPtr.Zero)
            {
                return;
            }

            try
            {
                var unused = Handle;
            }
            catch
            {
                // ignore
            }
        }

        private void GetPixelSize(out int width, out int height)
        {
            double aw = ActualWidth > 1 ? ActualWidth : (RenderSize.Width > 1 ? RenderSize.Width : 200);
            double ah = ActualHeight > 1 ? ActualHeight : (RenderSize.Height > 1 ? RenderSize.Height : 200);
            ToDevicePixels(aw, ah, out width, out height);
        }

        private void ToDevicePixels(double dipW, double dipH, out int width, out int height)
        {
            width = Math.Max(1, (int)Math.Round(dipW));
            height = Math.Max(1, (int)Math.Round(dipH));
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                Matrix m = source.CompositionTarget.TransformToDevice;
                width = Math.Max(1, (int)Math.Round(dipW * m.M11));
                height = Math.Max(1, (int)Math.Round(dipH * m.M22));
            }
        }
    }
}
