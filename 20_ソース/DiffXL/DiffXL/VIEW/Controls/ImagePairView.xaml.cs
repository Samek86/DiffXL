using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DiffXL.LOGIC.Diff;
using DiffXL.VIEW.Dialogs;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// 画像ペア 1 行の表示。自側画像を表示し、差分領域を
    /// 赤枠 3px ＋ 黄 50% 塗り（設定可）で Canvas 重ねする。
    /// HighlightVisible=false でも画像本体は残し、枠・塗りだけ消す。
    /// 左右両方ある場合のみクリックで重ね合わせ Window を開く。
    /// </summary>
    public partial class ImagePairView : UserControl
    {
        /// <summary>
        /// 表示幅の上限（px）。
        /// </summary>
        private const double MaxDisplayWidth = 320.0;

        /// <summary>
        /// 表示高さの上限（px）。
        /// </summary>
        private const double MaxDisplayHeight = 240.0;

        /// <summary>
        /// 保持中のハイライト領域（画像ローカル座標）。
        /// </summary>
        private List<HighlightRegion> _regions = new List<HighlightRegion>();

        /// <summary>
        /// 画像ピクセル幅（スケール計算用）。
        /// </summary>
        private int _pixelWidth;

        /// <summary>
        /// 画像ピクセル高さ。
        /// </summary>
        private int _pixelHeight;

        /// <summary>
        /// 表示スケール（表示 px / 画像 px）。
        /// </summary>
        private double _scale = 1.0;

        /// <summary>
        /// ハイライト表示フラグ。
        /// </summary>
        private bool _highlightVisible = true;

        /// <summary>
        /// 枠ブラシ（設定から）。
        /// </summary>
        private Brush _borderBrush;

        /// <summary>
        /// 塗りブラシ（設定から）。
        /// </summary>
        private Brush _fillBrush;

        /// <summary>
        /// 枠線幅（px）。
        /// </summary>
        private double _borderThickness = 3.0;

        /// <summary>
        /// 左画像の抽出パス（オーバーレイ用）。
        /// </summary>
        private string _leftPath;

        /// <summary>
        /// 右画像の抽出パス（オーバーレイ用）。
        /// </summary>
        private string _rightPath;

        /// <summary>
        /// 左画像の表示名。
        /// </summary>
        private string _leftLabel;

        /// <summary>
        /// 右画像の表示名。
        /// </summary>
        private string _rightLabel;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public ImagePairView()
        {
            InitializeComponent();
            ApplyStyleFromSettings();
            ImageFrame.MouseLeftButtonUp += ImageFrame_MouseLeftButtonUp;
            ImageFrame.Cursor = Cursors.Arrow;
        }

        /// <summary>
        /// ハイライト（枠・塗り）の表示／非表示。画像本体は常に残す。再比較不要。
        /// </summary>
        public bool HighlightVisible
        {
            get { return _highlightVisible; }
            set { SetHighlightVisible(value); }
        }

        /// <summary>
        /// ハイライト表示を切り替え、Canvas だけ更新する（再 Load 不要）。
        /// </summary>
        /// <param name="visible">表示するなら true</param>
        public void SetHighlightVisible(bool visible)
        {
            _highlightVisible = visible;
            RebuildHighlightCanvas();
        }

        /// <summary>
        /// 設定の画像ハイライト色・線幅を再読込して再描画する。
        /// </summary>
        public void RefreshStyleFromSettings()
        {
            ApplyStyleFromSettings();
            RebuildHighlightCanvas();
        }

        /// <summary>
        /// 左右画像と差分を読み込み、このペイン側を表示する。
        /// </summary>
        /// <param name="leftImage">左画像（null 可）</param>
        /// <param name="rightImage">右画像（null 可）</param>
        /// <param name="relatedDiff">対応 DiffItem（完全一致時は null 可）</param>
        /// <param name="isLeft">左ペインなら true</param>
        /// <param name="highlightVisible">ハイライト初期表示</param>
        public void Load(
            EmbeddedImage leftImage,
            EmbeddedImage rightImage,
            DiffItem relatedDiff,
            bool isLeft,
            bool highlightVisible)
        {
            _highlightVisible = highlightVisible;
            ApplyStyleFromSettings();
            _regions = new List<HighlightRegion>();

            _leftPath = leftImage != null ? leftImage.ExtractedPath : null;
            _rightPath = rightImage != null ? rightImage.ExtractedPath : null;
            _leftLabel = leftImage != null
                ? (leftImage.FileName ?? leftImage.PackagePath ?? "left")
                : null;
            _rightLabel = rightImage != null
                ? (rightImage.FileName ?? rightImage.PackagePath ?? "right")
                : null;
            UpdateOverlayCursor();

            EmbeddedImage self = isLeft ? leftImage : rightImage;
            EmbeddedImage partner = isLeft ? rightImage : leftImage;

            TitleText.Text = BuildTitle(self, partner, isLeft, relatedDiff);
            SubtitleText.Text = BuildSubtitle(leftImage, rightImage, relatedDiff);

            // ギャップ: 自側に画像が無い（相手のみ）
            if (self == null)
            {
                ShowGap(BuildGapMessage(isLeft, relatedDiff));
                return;
            }

            string path = self.ExtractedPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ShowGap("画像ファイルなし: " + (self.FileName ?? self.PackagePath ?? "?"));
                EmptyHint.Visibility = Visibility.Visible;
                EmptyHint.Text = "（抽出パスが無効です）";
                return;
            }

            BitmapImage bmp = TryLoadBitmap(path);
            if (bmp == null)
            {
                ShowGap("読込失敗: " + (self.FileName ?? path));
                EmptyHint.Visibility = Visibility.Visible;
                return;
            }

            int pw = self.PixelWidth > 0 ? self.PixelWidth : (int)bmp.PixelWidth;
            int ph = self.PixelHeight > 0 ? self.PixelHeight : (int)bmp.PixelHeight;
            if (pw <= 0)
            {
                pw = (int)bmp.PixelWidth;
            }

            if (ph <= 0)
            {
                ph = (int)bmp.PixelHeight;
            }

            if (pw <= 0)
            {
                pw = 1;
            }

            if (ph <= 0)
            {
                ph = 1;
            }

            _pixelWidth = pw;
            _pixelHeight = ph;
            double scaleW = MaxDisplayWidth / pw;
            double scaleH = MaxDisplayHeight / ph;
            _scale = Math.Min(1.0, Math.Min(scaleW, scaleH));
            double dispW = Math.Max(1.0, Math.Round(pw * _scale));
            double dispH = Math.Max(1.0, Math.Round(ph * _scale));

            GapBox.Visibility = Visibility.Collapsed;
            EmptyHint.Visibility = Visibility.Collapsed;
            ImageFrame.Visibility = Visibility.Visible;
            MainImage.Source = bmp;
            MainImage.Width = dispW;
            MainImage.Height = dispH;
            ImageGrid.Width = dispW;
            ImageGrid.Height = dispH;
            HighlightCanvas.Width = dispW;
            HighlightCanvas.Height = dispH;

            // Match 差分の領域のみハイライト対象（片側のみは枠無し）
            if (relatedDiff != null
                && relatedDiff.Kind == DiffKind.Image
                && relatedDiff.HighlightRegions != null
                && relatedDiff.HighlightRegions.Count > 0)
            {
                _regions = new List<HighlightRegion>(relatedDiff.HighlightRegions);
            }

            RebuildHighlightCanvas();
            UpdateOverlayCursor();
        }

        /// <summary>
        /// ギャップ表示に切り替える。
        /// </summary>
        private void ShowGap(string message)
        {
            GapText.Text = message ?? "（この側に画像なし）";
            GapBox.Visibility = Visibility.Visible;
            ImageFrame.Visibility = Visibility.Collapsed;
            MainImage.Source = null;
            HighlightCanvas.Children.Clear();
            _regions = new List<HighlightRegion>();
            UpdateOverlayCursor();
        }

        /// <summary>
        /// 左右両方の画像パスが有効なとき true。
        /// </summary>
        private bool CanOpenOverlay()
        {
            return !string.IsNullOrEmpty(_leftPath) && File.Exists(_leftPath)
                && !string.IsNullOrEmpty(_rightPath) && File.Exists(_rightPath);
        }

        /// <summary>
        /// クリック可能なとき Hand カーソルとツールチップを付ける。
        /// </summary>
        private void UpdateOverlayCursor()
        {
            if (ImageFrame == null)
            {
                return;
            }

            if (CanOpenOverlay())
            {
                ImageFrame.Cursor = Cursors.Hand;
                ImageFrame.ToolTip = "クリックで重ね合わせ比較を開く（OpenCV 位置合わせ）";
            }
            else
            {
                ImageFrame.Cursor = Cursors.Arrow;
                ImageFrame.ToolTip = null;
            }
        }

        /// <summary>
        /// 画像クリック → 左右両方ある場合のみオーバーレイ Window を開く。
        /// </summary>
        private void ImageFrame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!CanOpenOverlay())
            {
                return;
            }

            try
            {
                var win = new ImageOverlayWindow(_leftPath, _rightPath, _leftLabel, _rightLabel);
                Window owner = Window.GetWindow(this);
                if (owner != null)
                {
                    win.Owner = owner;
                }

                win.Show();
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "重ね合わせウィンドウを開けませんでした。\n" + ex.Message,
                    "DiffXL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// ハイライト Canvas を再構築する。OFF 時は子を空にする（データ _regions は保持）。
        /// </summary>
        private void RebuildHighlightCanvas()
        {
            if (HighlightCanvas == null)
            {
                return;
            }

            HighlightCanvas.Children.Clear();
            if (!_highlightVisible || _regions == null || _regions.Count == 0)
            {
                return;
            }

            if (_borderBrush == null || _fillBrush == null)
            {
                ApplyStyleFromSettings();
            }

            double scale = _scale > 0 ? _scale : 1.0;
            foreach (HighlightRegion region in _regions)
            {
                if (region == null || region.Width <= 0 || region.Height <= 0)
                {
                    continue;
                }

                double x = region.X * scale;
                double y = region.Y * scale;
                double w = Math.Max(1.0, region.Width * scale);
                double h = Math.Max(1.0, region.Height * scale);

                var rect = new Rectangle
                {
                    Width = w,
                    Height = h,
                    Fill = _fillBrush,
                    Stroke = _borderBrush,
                    StrokeThickness = _borderThickness,
                    IsHitTestVisible = false,
                    SnapsToDevicePixels = true
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                HighlightCanvas.Children.Add(rect);
            }
        }

        /// <summary>
        /// AppSettings の画像ハイライト色・線幅を適用する。
        /// </summary>
        private void ApplyStyleFromSettings()
        {
            try
            {
                DiffHighlightStyle style = DiffHighlightStyle.FromImageSettings();
                _borderBrush = style.CreateImageBorderBrush();
                _fillBrush = style.CreateImageFillBrush();
                _borderThickness = style.BorderThickness;
                if (_borderThickness < 0)
                {
                    _borderThickness = 0;
                }
            }
            catch
            {
                // 既定: 赤 3px 枠 / 黄 50% 塗り
                _borderBrush = CreateFrozenBrush(0xFF, 0xFF, 0x00, 0x00);
                _fillBrush = CreateFrozenBrush(0x80, 0xFF, 0xFF, 0x00);
                _borderThickness = 3.0;
            }
        }

        /// <summary>
        /// タイトル文字列。
        /// </summary>
        private static string BuildTitle(
            EmbeddedImage self,
            EmbeddedImage partner,
            bool isLeft,
            DiffItem relatedDiff)
        {
            string side = isLeft ? "左" : "右";
            string marker = "＝";
            if (relatedDiff != null)
            {
                if (relatedDiff.Kind == DiffKind.Image)
                {
                    marker = "± 部分差";
                }
                else if (relatedDiff.Kind == DiffKind.ImageOnlyLeft)
                {
                    marker = isLeft ? "− 左のみ" : "∅ 欠落";
                }
                else if (relatedDiff.Kind == DiffKind.ImageOnlyRight)
                {
                    marker = !isLeft ? "+ 右のみ" : "∅ 欠落";
                }
            }
            else if (self != null && partner == null)
            {
                marker = isLeft ? "− 左のみ" : "+ 右のみ";
            }
            else if (self == null && partner != null)
            {
                marker = "∅ 欠落";
            }

            string name = self != null
                ? (self.FileName ?? self.PackagePath ?? "?")
                : "（なし）";
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} · {2}",
                side,
                marker,
                name);
        }

        /// <summary>
        /// サブタイトル（寸法・領域数）。
        /// </summary>
        private static string BuildSubtitle(
            EmbeddedImage leftImage,
            EmbeddedImage rightImage,
            DiffItem relatedDiff)
        {
            string leftLabel = FormatImageShort(leftImage);
            string rightLabel = FormatImageShort(rightImage);
            int regionCount = relatedDiff != null && relatedDiff.HighlightRegions != null
                ? relatedDiff.HighlightRegions.Count
                : 0;
            string kind = relatedDiff != null ? relatedDiff.Kind.ToString() : "Match";
            bool both = leftImage != null && rightImage != null
                && !string.IsNullOrEmpty(leftImage.ExtractedPath)
                && !string.IsNullOrEmpty(rightImage.ExtractedPath)
                && File.Exists(leftImage.ExtractedPath)
                && File.Exists(rightImage.ExtractedPath);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} ↔ {1} · {2} · regions={3}{4}",
                leftLabel,
                rightLabel,
                kind,
                regionCount,
                both ? " · クリックで重ね合わせ" : string.Empty);
        }

        /// <summary>
        /// ギャップメッセージ。
        /// </summary>
        private static string BuildGapMessage(bool isLeft, DiffItem relatedDiff)
        {
            if (relatedDiff != null)
            {
                if (relatedDiff.Kind == DiffKind.ImageOnlyLeft && !isLeft)
                {
                    return "∅ 左にのみ存在する画像（この側は空）";
                }

                if (relatedDiff.Kind == DiffKind.ImageOnlyRight && isLeft)
                {
                    return "∅ 右にのみ存在する画像（この側は空）";
                }
            }

            return isLeft
                ? "（左に画像なし）"
                : "（右に画像なし）";
        }

        /// <summary>
        /// 画像の短い表示ラベル。
        /// </summary>
        private static string FormatImageShort(EmbeddedImage img)
        {
            if (img == null)
            {
                return "—";
            }

            string name = img.FileName ?? img.PackagePath ?? "?";
            if (img.PixelWidth > 0 && img.PixelHeight > 0)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} ({1}x{2})",
                    name,
                    img.PixelWidth,
                    img.PixelHeight);
            }

            return name;
        }

        /// <summary>
        /// 画像ファイルを BitmapImage として読み込む。
        /// </summary>
        private static BitmapImage TryLoadBitmap(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
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

        /// <summary>
        /// 凍結ソリッドブラシ。
        /// </summary>
        private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
