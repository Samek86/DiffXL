using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW.Dialogs
{
    /// <summary>
    /// 左右画像を OpenCV 位置合わせ後に重ね、F5 切替・拡大で差分を目視確認する Window。
    /// UI は MainWindow と同じ ToolBar / Primary / Toggle スタイルを使用する。
    /// </summary>
    public partial class ImageOverlayWindow : Window
    {
        private const double ZoomMin = 10;
        private const double ZoomMax = 1400;
        private const double OriginPad = 200;

        private readonly string _leftPath;
        private readonly string _rightPath;
        private readonly string _leftLabel;
        private readonly string _rightLabel;

        private enum ActiveSide
        {
            Left,
            Right
        }

        private ActiveSide _active = ActiveSide.Left;
        private bool _overlap;
        private bool _loaded;
        private bool _zoomSliderSilent;
        private bool _panning;
        private Point _panStart;
        private Point _scrollStart;

        private double _zoom = 100;
        private double _manualDx;
        private double _manualDy;
        private int _imageWidth;
        private int _imageHeight;
        private string _statusBase = string.Empty;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public ImageOverlayWindow(
            string leftPath,
            string rightPath,
            string leftLabel = null,
            string rightLabel = null)
        {
            InitializeComponent();
            _leftPath = leftPath;
            _rightPath = rightPath;
            _leftLabel = string.IsNullOrEmpty(leftLabel)
                ? Path.GetFileName(leftPath)
                : leftLabel;
            _rightLabel = string.IsNullOrEmpty(rightLabel)
                ? Path.GetFileName(rightPath)
                : rightLabel;

            BtnLeftLabel.Text = "左 · " + _leftLabel;
            BtnRightLabel.Text = "右 · " + _rightLabel;
            Title = "画像重ね合わせ — " + _leftLabel + " ↔ " + _rightLabel;
            FooterText.Text = _leftLabel + " ↔ " + _rightLabel;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;
            BeginAlignAndLoad();
        }

        private void BeginAlignAndLoad()
        {
            SetLoading(true, "OpenCV で位置を合わせています…");
            string left = _leftPath;
            string right = _rightPath;

            Task.Run(() =>
            {
                ImageOverlayAlignResult result = ImageOverlayAligner.Align(left, right);
                Dispatcher.Invoke(() => ApplyAlignResult(result));
            });
        }

        private void ApplyAlignResult(ImageOverlayAlignResult result)
        {
            if (result == null || result.LeftPng == null || result.RightPng == null)
            {
                SetLoading(false, null);
                _statusBase = result != null && !string.IsNullOrEmpty(result.ErrorMessage)
                    ? result.ErrorMessage
                    : "画像の読み込みに失敗しました。";
                RefreshStatusText();
                return;
            }

            BitmapSource leftBmp = LoadPng(result.LeftPng);
            BitmapSource rightBmp = LoadPng(result.RightPng);
            if (leftBmp == null || rightBmp == null)
            {
                SetLoading(false, null);
                _statusBase = "表示用ビットマップの生成に失敗しました。";
                RefreshStatusText();
                return;
            }

            _imageWidth = result.Width > 0 ? result.Width : leftBmp.PixelWidth;
            _imageHeight = result.Height > 0 ? result.Height : leftBmp.PixelHeight;
            _manualDx = 0;
            _manualDy = 0;

            ImageLeft.Source = leftBmp;
            ImageRight.Source = rightBmp;
            ImageLeft.Width = _imageWidth;
            ImageLeft.Height = _imageHeight;
            ImageRight.Width = _imageWidth;
            ImageRight.Height = _imageHeight;

            double canvasW = _imageWidth + OriginPad * 2;
            double canvasH = _imageHeight + OriginPad * 2;
            ImageRoot.Width = canvasW;
            ImageRoot.Height = canvasH;
            LayerLeft.Width = canvasW;
            LayerLeft.Height = canvasH;
            LayerRight.Width = canvasW;
            LayerRight.Height = canvasH;

            Canvas.SetLeft(ImageLeft, OriginPad);
            Canvas.SetTop(ImageLeft, OriginPad);
            ApplyManualOffset();

            _overlap = false;
            _active = ActiveSide.Left;
            ApplyLayerState();

            SetZoom(100, updateSlider: true);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CenterView();
                SetLoading(false, null);
                _statusBase = result.Aligned
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "位置合わせ: {0}  shift=({1:0.##},{2:0.##}) conf={3:0.###}",
                        result.Method,
                        result.ShiftX,
                        result.ShiftY,
                        result.Confidence)
                    : "位置合わせ: 左上揃え（自動合わせ失敗）";
                RefreshStatusText();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ApplyManualOffset()
        {
            Canvas.SetLeft(ImageRight, OriginPad + _manualDx);
            Canvas.SetTop(ImageRight, OriginPad + _manualDy);
        }

        private void SetLoading(bool on, string detail)
        {
            LoadingOverlay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            LoadingDetail.Text = detail ?? string.Empty;
            BtnRealign.IsEnabled = !on;
            BtnToggle.IsEnabled = !on;
        }

        /// <summary>
        /// 前面レイヤを切り替える（非 Overlap 時は背面を Hidden）。
        /// </summary>
        private void ApplyLayerState()
        {
            if (_overlap)
            {
                LayerLeft.Visibility = Visibility.Visible;
                LayerRight.Visibility = Visibility.Visible;
                if (_active == ActiveSide.Left)
                {
                    Panel.SetZIndex(LayerLeft, 2);
                    Panel.SetZIndex(LayerRight, 1);
                    LayerLeft.Opacity = 0.5;
                    LayerRight.Opacity = 1.0;
                }
                else
                {
                    Panel.SetZIndex(LayerRight, 2);
                    Panel.SetZIndex(LayerLeft, 1);
                    LayerRight.Opacity = 0.5;
                    LayerLeft.Opacity = 1.0;
                }
            }
            else
            {
                LayerLeft.Opacity = 1.0;
                LayerRight.Opacity = 1.0;
                if (_active == ActiveSide.Left)
                {
                    LayerLeft.Visibility = Visibility.Visible;
                    LayerRight.Visibility = Visibility.Hidden;
                    Panel.SetZIndex(LayerLeft, 2);
                    Panel.SetZIndex(LayerRight, 1);
                }
                else
                {
                    LayerRight.Visibility = Visibility.Visible;
                    LayerLeft.Visibility = Visibility.Hidden;
                    Panel.SetZIndex(LayerRight, 2);
                    Panel.SetZIndex(LayerLeft, 1);
                }
            }

            // ToolBarToggleButtonStyle の IsChecked で選択状態を表現
            if (_overlap)
            {
                BtnLeft.IsChecked = true;
                BtnRight.IsChecked = true;
            }
            else
            {
                BtnLeft.IsChecked = _active == ActiveSide.Left;
                BtnRight.IsChecked = _active == ActiveSide.Right;
            }

            BtnOverlap.IsChecked = _overlap;
            BtnOverlapLabel.Text = _overlap ? "重ね合わせ ON" : "重ね合わせ";
            RefreshStatusText();
        }

        private void RefreshStatusText()
        {
            string side = _active == ActiveSide.Left ? "左" : "右";
            string mode = _overlap ? "重ね合わせ" : "単独";
            string hint = "表示: " + side + " (" + mode + ")";
            if (string.IsNullOrWhiteSpace(_statusBase))
            {
                StatusText.Text = hint;
            }
            else
            {
                StatusText.Text = _statusBase + "  |  " + hint;
            }

            FooterText.Text = _leftLabel + " ↔ " + _rightLabel + "  ·  " + hint;
        }

        private void ToggleActive()
        {
            _active = _active == ActiveSide.Left ? ActiveSide.Right : ActiveSide.Left;
            ApplyLayerState();
        }

        private void SetZoom(double zoom, bool updateSlider)
        {
            _zoom = Math.Max(ZoomMin, Math.Min(ZoomMax, zoom));
            double scale = _zoom / 100.0;
            CanvasScale.ScaleX = scale;
            CanvasScale.ScaleY = scale;
            ZoomLabel.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.#}%", _zoom);
            if (updateSlider)
            {
                _zoomSliderSilent = true;
                ZoomSlider.Value = _zoom;
                _zoomSliderSilent = false;
            }
        }

        private void CenterView()
        {
            if (_imageWidth <= 0 || _imageHeight <= 0)
            {
                return;
            }

            double scale = _zoom / 100.0;
            double imgX = (OriginPad + _imageWidth / 2.0) * scale;
            double imgY = (OriginPad + _imageHeight / 2.0) * scale;
            double viewW = ScrollHost.ViewportWidth;
            double viewH = ScrollHost.ViewportHeight;
            if (viewW <= 0 || viewH <= 0)
            {
                viewW = ActualWidth;
                viewH = Math.Max(100, ActualHeight - 80);
            }

            ScrollHost.ScrollToHorizontalOffset(Math.Max(0, imgX - viewW / 2.0));
            ScrollHost.ScrollToVerticalOffset(Math.Max(0, imgY - viewH / 2.0));
        }

        private void FitView()
        {
            if (_imageWidth <= 0 || _imageHeight <= 0)
            {
                return;
            }

            double viewW = ScrollHost.ViewportWidth;
            double viewH = ScrollHost.ViewportHeight;
            if (viewW <= 40 || viewH <= 40)
            {
                viewW = Math.Max(200, ActualWidth - 40);
                viewH = Math.Max(200, ActualHeight - 120);
            }

            double ratioX = viewW / _imageWidth;
            double ratioY = viewH / _imageHeight;
            double ratio = Math.Min(ratioX, ratioY);
            SetZoom(100.0 * ratio, updateSlider: true);
            CenterView();
        }

        private void NudgeManual(double dx, double dy)
        {
            if (_active == ActiveSide.Left)
            {
                _manualDx -= dx;
                _manualDy -= dy;
            }
            else
            {
                _manualDx += dx;
                _manualDy += dy;
            }

            ApplyManualOffset();
            _statusBase = string.Format(
                CultureInfo.InvariantCulture,
                "手動オフセット: ({0:0.##}, {1:0.##}) px",
                _manualDx,
                _manualDy);
            RefreshStatusText();
        }

        private void BtnLeft_Click(object sender, RoutedEventArgs e)
        {
            _active = ActiveSide.Left;
            ApplyLayerState();
        }

        private void BtnRight_Click(object sender, RoutedEventArgs e)
        {
            _active = ActiveSide.Right;
            ApplyLayerState();
        }

        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            ToggleActive();
        }

        private void BtnOverlap_Click(object sender, RoutedEventArgs e)
        {
            _overlap = !_overlap;
            ApplyLayerState();
        }

        private void BtnRealign_Click(object sender, RoutedEventArgs e)
        {
            BeginAlignAndLoad();
        }

        private void BtnCenter_Click(object sender, RoutedEventArgs e)
        {
            CenterView();
        }

        private void Btn100_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(100, updateSlider: true);
            CenterView();
        }

        private void BtnFit_Click(object sender, RoutedEventArgs e)
        {
            FitView();
        }

        private void BtnZoomMinus_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(_zoom - 10, updateSlider: true);
        }

        private void BtnZoomPlus_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(_zoom + 10, updateSlider: true);
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded || _zoomSliderSilent)
            {
                return;
            }

            SetZoom(e.NewValue, updateSlider: false);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            double jump = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 10.0 : 1.0;
            switch (e.Key)
            {
                case Key.Right:
                    NudgeManual(jump, 0);
                    e.Handled = true;
                    break;
                case Key.Left:
                    NudgeManual(-jump, 0);
                    e.Handled = true;
                    break;
                case Key.Up:
                    NudgeManual(0, -jump);
                    e.Handled = true;
                    break;
                case Key.Down:
                    NudgeManual(0, jump);
                    e.Handled = true;
                    break;
                case Key.F5:
                    e.Handled = true;
                    break;
            }
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                ToggleActive();
                e.Handled = true;
            }
        }

        private void PanLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _panning = true;
            _panStart = e.GetPosition(PanLayer);
            _scrollStart = new Point(ScrollHost.HorizontalOffset, ScrollHost.VerticalOffset);
            PanLayer.CaptureMouse();
            e.Handled = true;
        }

        private void PanLayer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_panning)
            {
                _panning = false;
                PanLayer.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void PanLayer_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_panning)
            {
                return;
            }

            Point now = e.GetPosition(PanLayer);
            ScrollHost.ScrollToHorizontalOffset(_scrollStart.X - (now.X - _panStart.X));
            ScrollHost.ScrollToVerticalOffset(_scrollStart.Y - (now.Y - _panStart.Y));
        }

        private void PanLayer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                double jump = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 10.0 : 1.0;
                if (e.Delta > 0)
                {
                    SetZoom(_zoom + jump, updateSlider: true);
                }
                else
                {
                    SetZoom(_zoom - jump, updateSlider: true);
                }

                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                ScrollHost.ScrollToHorizontalOffset(ScrollHost.HorizontalOffset - e.Delta);
            }
            else
            {
                ScrollHost.ScrollToVerticalOffset(ScrollHost.VerticalOffset - e.Delta);
            }

            e.Handled = true;
        }

        private static BitmapSource LoadPng(byte[] png)
        {
            if (png == null || png.Length == 0)
            {
                return null;
            }

            try
            {
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(png))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }

                if (bmp.CanFreeze)
                {
                    bmp.Freeze();
                }

                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
