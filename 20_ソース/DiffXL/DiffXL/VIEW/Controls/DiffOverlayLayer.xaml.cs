using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DiffXL.LOGIC.Diff;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// オーバーレイ 1 要素。
    /// </summary>
    public sealed class OverlayShape
    {
        /// <summary>
        /// レイヤローカル座標の境界。
        /// </summary>
        public Rect Bounds { get; set; }

        /// <summary>
        /// ツールチップ。
        /// </summary>
        public string ToolTip { get; set; }

        /// <summary>
        /// 画像差分マスクパス（任意）。
        /// </summary>
        public string MaskImagePath { get; set; }
    }

    /// <summary>
    /// 差分強調を Canvas 上に描画するレイヤ。
    /// Excel 操作を奪わないようヒットテスト無効。
    /// </summary>
    public partial class DiffOverlayLayer : UserControl
    {
        /// <summary>
        /// 表示中の図形データ。
        /// </summary>
        private readonly List<OverlayShape> _shapes = new List<OverlayShape>();

        /// <summary>
        /// 塗りつぶしブラシ。
        /// </summary>
        private Brush _fillBrush = CreateDefaultFill();

        /// <summary>
        /// 枠線ブラシ。
        /// </summary>
        private Brush _borderBrush = CreateDefaultBorder();

        /// <summary>
        /// 強調表示するか。
        /// </summary>
        private bool _isHighlightVisible = true;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public DiffOverlayLayer()
        {
            InitializeComponent();
            Opacity = 1.0;
        }

        /// <summary>
        /// 差分強調の表示／非表示。OFF でもデータは保持する。
        /// </summary>
        public bool IsHighlightVisible
        {
            get { return _isHighlightVisible; }
            set
            {
                _isHighlightVisible = value;
                // Collapsed ではなく Opacity 0（データ保持・再表示が即時）
                Opacity = value ? 1.0 : 0.0;
            }
        }

        /// <summary>
        /// 強調スタイルを適用して再描画する。
        /// </summary>
        /// <param name="style">スタイル</param>
        public void ApplyStyle(DiffHighlightStyle style)
        {
            if (style == null)
            {
                style = DiffHighlightStyle.FromSettings();
            }

            _fillBrush = style.CreateBrush();
            _borderBrush = style.CreateBorderBrush();
            RebuildVisuals();
        }

        /// <summary>
        /// 図形を設定する。
        /// </summary>
        /// <param name="shapes">図形</param>
        public void SetItems(IEnumerable<OverlayShape> shapes)
        {
            _shapes.Clear();
            if (shapes != null)
            {
                _shapes.AddRange(shapes);
            }

            RebuildVisuals();
        }

        /// <summary>
        /// 図形データを消す。
        /// </summary>
        public void Clear()
        {
            _shapes.Clear();
            ShapeCanvas.Children.Clear();
        }

        /// <summary>
        /// Canvas 上のビジュアルを再構築する。
        /// </summary>
        private void RebuildVisuals()
        {
            ShapeCanvas.Children.Clear();
            foreach (OverlayShape shape in _shapes)
            {
                if (shape == null)
                {
                    continue;
                }

                Rect b = shape.Bounds;
                if (b.Width <= 0 || b.Height <= 0)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(shape.MaskImagePath) && File.Exists(shape.MaskImagePath))
                {
                    try
                    {
                        var image = new Image
                        {
                            Width = b.Width,
                            Height = b.Height,
                            Stretch = Stretch.Fill,
                            Opacity = 0.85,
                            IsHitTestVisible = false,
                            Source = LoadBitmap(shape.MaskImagePath)
                        };
                        // マスクを黄色寄りに見せるため半透明矩形を重ねる
                        var overlay = new System.Windows.Shapes.Rectangle
                        {
                            Width = b.Width,
                            Height = b.Height,
                            Fill = _fillBrush,
                            Stroke = _borderBrush,
                            StrokeThickness = 1,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(image, b.X);
                        Canvas.SetTop(image, b.Y);
                        Canvas.SetLeft(overlay, b.X);
                        Canvas.SetTop(overlay, b.Y);
                        ShapeCanvas.Children.Add(image);
                        ShapeCanvas.Children.Add(overlay);
                        if (!string.IsNullOrEmpty(shape.ToolTip))
                        {
                            ToolTipService.SetToolTip(overlay, shape.ToolTip);
                        }

                        continue;
                    }
                    catch
                    {
                        // 矩形フォールバック
                    }
                }

                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = b.Width,
                    Height = b.Height,
                    Fill = _fillBrush,
                    Stroke = _borderBrush,
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, b.X);
                Canvas.SetTop(rect, b.Y);
                if (!string.IsNullOrEmpty(shape.ToolTip))
                {
                    // ヒット無効のためツールチップは出にくい。データとしては保持。
                    ToolTipService.SetToolTip(rect, shape.ToolTip);
                }

                ShapeCanvas.Children.Add(rect);
            }
        }

        /// <summary>
        /// 画像ファイルを BitmapImage として読み込む。
        /// </summary>
        private static ImageSource LoadBitmap(string path)
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

        /// <summary>
        /// 既定の黄 50% 塗り。
        /// </summary>
        private static Brush CreateDefaultFill()
        {
            // ガター上でよく見える黄色（不透明度高め）
            var brush = new SolidColorBrush(Color.FromArgb(220, 250, 204, 21));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 既定の枠。
        /// </summary>
        private static Brush CreateDefaultBorder()
        {
            var brush = new SolidColorBrush(Color.FromRgb(234, 179, 8));
            brush.Freeze();
            return brush;
        }
    }
}
