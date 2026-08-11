using System;
using System.Globalization;
using System.Windows.Media;
using DiffXL.COMMON;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 差分強調の色と不透明度を表す。
    /// </summary>
    public sealed class DiffHighlightStyle
    {
        /// <summary>
        /// 赤成分。
        /// </summary>
        public byte R { get; set; } = 255;

        /// <summary>
        /// 緑成分。
        /// </summary>
        public byte G { get; set; } = 255;

        /// <summary>
        /// 青成分。
        /// </summary>
        public byte B { get; set; } = 0;

        /// <summary>
        /// 不透明度 0.0〜1.0。既定 0.5。
        /// </summary>
        public double Opacity { get; set; } = 0.5;

        /// <summary>
        /// 設定からスタイルを生成する。
        /// </summary>
        /// <returns>スタイル</returns>
        public static DiffHighlightStyle FromSettings()
        {
            DiffSettings d = AppSettings.Current != null ? AppSettings.Current.Diff : null;
            var style = new DiffHighlightStyle();
            if (d == null)
            {
                return style;
            }

            style.Opacity = d.HighlightOpacity;
            byte r;
            byte g;
            byte b;
            ParseHexRgbColor(d.HighlightColor, out r, out g, out b);
            style.R = r;
            style.G = g;
            style.B = b;
            if (style.Opacity < 0)
            {
                style.Opacity = 0;
            }

            if (style.Opacity > 1)
            {
                style.Opacity = 1;
            }

            return style;
        }

        /// <summary>
        /// WPF Color（アルファ込み）を返す。
        /// </summary>
        /// <returns>色</returns>
        public Color ToWpfColor()
        {
            byte a = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(Opacity * 255)));
            return Color.FromArgb(a, R, G, B);
        }

        /// <summary>
        /// 半透明ブラシを生成する。
        /// </summary>
        /// <returns>ブラシ</returns>
        public SolidColorBrush CreateBrush()
        {
            var brush = new SolidColorBrush(ToWpfColor());
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }

        /// <summary>
        /// 枠線用（不透明寄り）ブラシを生成する。
        /// </summary>
        /// <returns>ブラシ</returns>
        public SolidColorBrush CreateBorderBrush()
        {
            byte a = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(Math.Min(1.0, Opacity + 0.35) * 255)));
            var brush = new SolidColorBrush(Color.FromArgb(a, R, G, B));
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }

        /// <summary>
        /// #RRGGBB / #AARRGGBB / RRGGBB を解析する。
        /// </summary>
        /// <param name="hex">色文字列</param>
        /// <param name="r">R</param>
        /// <param name="g">G</param>
        /// <param name="b">B</param>
        public static void ParseHexRgbColor(string hex, out byte r, out byte g, out byte b)
        {
            r = 255;
            g = 255;
            b = 0;
            if (string.IsNullOrWhiteSpace(hex))
            {
                return;
            }

            string s = hex.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal))
            {
                s = s.Substring(1);
            }

            try
            {
                if (s.Length == 8)
                {
                    // AARRGGBB → RGB のみ使用（不透明度は設定側）
                    r = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    g = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    b = byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                else if (s.Length == 6)
                {
                    r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                else if (s.Length == 3)
                {
                    r = byte.Parse(new string(s[0], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    g = byte.Parse(new string(s[1], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    b = byte.Parse(new string(s[2], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                r = 255;
                g = 255;
                b = 0;
            }
        }

        /// <summary>
        /// #RRGGBB 文字列を返す。
        /// </summary>
        /// <returns>色文字列</returns>
        public string ToHexRgb()
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", R, G, B);
        }
    }
}
