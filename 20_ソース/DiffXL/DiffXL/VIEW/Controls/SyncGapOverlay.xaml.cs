using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DiffXL.COMMON;
using DiffXL.LOGIC.Excel;
using MahApps.Metro.IconPacks;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// 内容同期ギャップ時に Excel ホスト上へ載せる半透明説明オーバーレイ。
    /// HwndHost airspace 回避のため Popup（別 HWND）+ クリック透過。
    /// 表示切替は即時（フェードなし / ReduceMotion 相当）。
    /// </summary>
    public partial class SyncGapOverlay : UserControl
    {
        private const int GwlExstyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExLayered = 0x00080000;

        private FrameworkElement _sizeSource;
        private bool _clickThroughApplied;

        public SyncGapOverlay()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
            SizeChanged += OnSelfSizeChanged;
        }

        /// <summary>
        /// 同期状態を左右ペイン向けに反映する。
        /// isLeftPane=true かつ RightOnly → 左「待機」。
        /// isLeftPane=false かつ LeftOnly → 右「待機」。
        /// 相手のみ側は「比較相手にない内容」。Equal / Disabled 等は非表示。
        /// </summary>
        public void Apply(SyncSessionState state, bool isLeftPane)
        {
            bool showSetting = AppSettings.Current == null
                || AppSettings.Current.Ui == null
                || AppSettings.Current.Ui.ShowSyncGapOverlay;

            if (!showSetting || state == null || !state.Enabled || !state.IsInGap)
            {
                HideOverlay();
                return;
            }

            SyncSegmentKind kind = state.SegmentKind;
            bool isWaitingSide =
                (isLeftPane && kind == SyncSegmentKind.RightOnly)
                || (!isLeftPane && kind == SyncSegmentKind.LeftOnly);
            bool isOnlySide =
                (isLeftPane && kind == SyncSegmentKind.LeftOnly)
                || (!isLeftPane && kind == SyncSegmentKind.RightOnly);

            if (!isWaitingSide && !isOnlySide)
            {
                HideOverlay();
                return;
            }

            if (isWaitingSide)
            {
                TitleText.Text = "こちらで待機中";
                Icon.Kind = PackIconPhosphorIconsKind.Pause;
            }
            else
            {
                TitleText.Text = "比較相手にない内容";
                Icon.Kind = PackIconPhosphorIconsKind.ImageBroken;
            }

            CaptionText.Text = string.IsNullOrEmpty(state.GapCaption)
                ? string.Empty
                : state.GapCaption;

            ShowOverlay();
        }

        private void ShowOverlay()
        {
            EnsureSizeSource();
            SyncPopupSize();
            if (!OverlayPopup.IsOpen)
            {
                _clickThroughApplied = false;
                OverlayPopup.IsOpen = true;
            }

            // フェードなし即表示
            Dispatcher.BeginInvoke(new Action(ApplyClickThrough), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void HideOverlay()
        {
            if (OverlayPopup != null && OverlayPopup.IsOpen)
            {
                OverlayPopup.IsOpen = false;
            }

            _clickThroughApplied = false;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            EnsureSizeSource();
            SyncPopupSize();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachSizeSource();
            HideOverlay();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                EnsureSizeSource();
                SyncPopupSize();
            }
        }

        private void EnsureSizeSource()
        {
            FrameworkElement parent = Parent as FrameworkElement;
            if (parent == null)
            {
                return;
            }

            if (ReferenceEquals(_sizeSource, parent))
            {
                return;
            }

            DetachSizeSource();
            _sizeSource = parent;
            _sizeSource.SizeChanged += SizeSource_SizeChanged;
        }

        private void DetachSizeSource()
        {
            if (_sizeSource != null)
            {
                _sizeSource.SizeChanged -= SizeSource_SizeChanged;
                _sizeSource = null;
            }
        }

        private void SizeSource_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SyncPopupSize();
        }

        private void OnSelfSizeChanged(object sender, SizeChangedEventArgs e)
        {
            SyncPopupSize();
        }

        /// <summary>
        /// 親／ホスト矩形に Popup を合わせる（リサイズ・DPI 後もカード中央維持）。
        /// </summary>
        private void SyncPopupSize()
        {
            FrameworkElement source = _sizeSource ?? Parent as FrameworkElement;
            if (source == null || OverlayRoot == null || OverlayPopup == null)
            {
                return;
            }

            double w = source.ActualWidth;
            double h = source.ActualHeight;
            if (w < 1 || h < 1)
            {
                // 自己 Actual があればフォールバック（最小化復帰など）
                if (ActualWidth >= 1 && ActualHeight >= 1)
                {
                    w = ActualWidth;
                    h = ActualHeight;
                }
                else
                {
                    return;
                }
            }

            OverlayRoot.Width = w;
            OverlayRoot.Height = h;
            // Placement を揺らし Popup を再配置（リサイズ後のずれ対策）
            OverlayPopup.HorizontalOffset = 0.001;
            OverlayPopup.VerticalOffset = 0.001;
            OverlayPopup.HorizontalOffset = 0;
            OverlayPopup.VerticalOffset = 0;
        }

        /// <summary>
        /// Popup HWND に WS_EX_TRANSPARENT を付与し、Excel へのマウス入力を阻害しない。
        /// </summary>
        private void ApplyClickThrough()
        {
            if (_clickThroughApplied || OverlayRoot == null || !OverlayPopup.IsOpen)
            {
                return;
            }

            try
            {
                HwndSource source = PresentationSource.FromVisual(OverlayRoot) as HwndSource;
                if (source == null || source.Handle == IntPtr.Zero)
                {
                    return;
                }

                int ex = GetWindowLong(source.Handle, GwlExstyle);
                SetWindowLong(source.Handle, GwlExstyle, ex | WsExTransparent | WsExLayered);
                _clickThroughApplied = true;
            }
            catch
            {
                // 透過付与失敗でも表示は継続（クリックは IsHitTestVisible=false に依存）
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
