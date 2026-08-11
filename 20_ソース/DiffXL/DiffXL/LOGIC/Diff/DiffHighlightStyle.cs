using System;
using System.Globalization;
using System.Windows.Media;
using DiffXL.COMMON;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// 差分強調の色と不透明度を表す（セル用＋画像枠／塗り用）。
    /// </summary>
    public sealed class DiffHighlightStyle
    {
        /// <summary>
        /// 赤成分（セル強調／互換用）。
        /// </summary>
        public byte R { get; set; } = 255;

        /// <summary>
        /// 緑成分（セル強調／互換用）。
        /// </summary>
        public byte G { get; set; } = 255;

        /// <summary>
        /// 青成分（セル強調／互換用）。
        /// </summary>
        public byte B { get; set; } = 0;

        /// <summary>
        /// 不透明度 0.0〜1.0。既定 0.5（セル強調用）。
        /// </summary>
        public double Opacity { get; set; } = 0.5;

        /// <summary>
        /// 画像枠のアルファ。
        /// </summary>
        public byte BorderA { get; set; } = 255;

        /// <summary>
        /// 画像枠の赤。
        /// </summary>
        public byte BorderR { get; set; } = 255;

        /// <summary>
        /// 画像枠の緑。
        /// </summary>
        public byte BorderG { get; set; } = 0;

        /// <summary>
        /// 画像枠の青。
        /// </summary>
        public byte BorderB { get; set; } = 0;

        /// <summary>
        /// 画像塗りのアルファ（既定 0x80 ≒ 50%）。
        /// </summary>
        public byte FillA { get; set; } = 0x80;

        /// <summary>
        /// 画像塗りの赤。
        /// </summary>
        public byte FillR { get; set; } = 255;

        /// <summary>
        /// 画像塗りの緑。
        /// </summary>
        public byte FillG { get; set; } = 255;

        /// <summary>
        /// 画像塗りの青。
        /// </summary>
        public byte FillB { get; set; } = 0;

        /// <summary>
        /// 画像枠の線幅（px）。既定 3。
        /// </summary>
        public int BorderThickness { get; set; } = 3;

        /// <summary>
        /// 設定からセル用スタイルを生成する。
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

            ApplyImageSettings(style, d);
            return style;
        }

        /// <summary>
        /// 設定から画像ハイライト用スタイルを生成する（枠・塗り・線幅を優先反映）。
        /// </summary>
        /// <returns>画像用スタイル</returns>
        public static DiffHighlightStyle FromImageSettings()
        {
            DiffSettings d = AppSettings.Current != null ? AppSettings.Current.Diff : null;
            var style = new DiffHighlightStyle();
            if (d == null)
            {
                return style;
            }

            ApplyImageSettings(style, d);
            // 画像塗り α を Opacity にも載せ、既存 CreateBrush 経路でも使えるようにする
            style.Opacity = style.FillA / 255.0;
            style.R = style.FillR;
            style.G = style.FillG;
            style.B = style.FillB;
            return style;
        }

        /// <summary>
        /// DiffSettings の画像ハイライト項目をスタイルへ適用する。
        /// </summary>
        /// <param name="style">対象</param>
        /// <param name="d">設定</param>
        private static void ApplyImageSettings(DiffHighlightStyle style, DiffSettings d)
        {
            if (style == null || d == null)
            {
                return;
            }

            byte a, r, g, b;
            ParseHexArgbColor(d.ImageHighlightBorderColor, out a, out r, out g, out b);
            style.BorderA = a;
            style.BorderR = r;
            style.BorderG = g;
            style.BorderB = b;

            ParseHexArgbColor(d.ImageHighlightFillColor, out a, out r, out g, out b);
            style.FillA = a;
            style.FillR = r;
            style.FillG = g;
            style.FillB = b;

            style.BorderThickness = d.ImageHighlightBorderThickness;
            if (style.BorderThickness < 0)
            {
                style.BorderThickness = 0;
            }
        }

        /// <summary>
        /// WPF Color（アルファ込み）を返す（セル強調用）。
        /// </summary>
        /// <returns>色</returns>
        public Color ToWpfColor()
        {
            byte a = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(Opacity * 255)));
            return Color.FromArgb(a, R, G, B);
        }

        /// <summary>
        /// 半透明ブラシを生成する（セル強調用）。
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
        /// 枠線用（不透明寄り）ブラシを生成する（セル強調用）。
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
        /// 画像ハイライト枠用ブラシを生成する。
        /// </summary>
        /// <returns>ブラシ</returns>
        public SolidColorBrush CreateImageBorderBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(BorderA, BorderR, BorderG, BorderB));
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }

        /// <summary>
        /// 画像ハイライト塗り用ブラシを生成する。
        /// </summary>
        /// <returns>ブラシ</returns>
        public SolidColorBrush CreateImageFillBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(FillA, FillR, FillG, FillB));
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }

        /// <summary>
        /// #RRGGBB / #AARRGGBB / RRGGBB を解析する（RGB のみ。A は無視）。
        /// </summary>
        /// <param name="hex">色文字列</param>
        /// <param name="r">R</param>
        /// <param name="g">G</param>
        /// <param name="b">B</param>
        public static void ParseHexRgbColor(string hex, out byte r, out byte g, out byte b)
        {
            byte a;
            ParseHexArgbColor(hex, out a, out r, out g, out b);
        }

        /// <summary>
        /// #RRGGBB / #AARRGGBB / 短縮形を解析する（A 既定は 0xFF）。
        /// </summary>
        /// <param name="hex">色文字列</param>
        /// <param name="a">A</param>
        /// <param name="r">R</param>
        /// <param name="g">G</param>
        /// <param name="b">B</param>
        public static void ParseHexArgbColor(string hex, out byte a, out byte r, out byte g, out byte b)
        {
            a = 255;
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
                    a = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    r = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    g = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    b = byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                else if (s.Length == 6)
                {
                    a = 255;
                    r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                else if (s.Length == 3)
                {
                    a = 255;
                    r = byte.Parse(new string(s[0], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    g = byte.Parse(new string(s[1], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    b = byte.Parse(new string(s[2], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                a = 255;
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

        /// <summary>
        /// 画像枠色を #AARRGGBB で返す。
        /// </summary>
        /// <returns>色文字列</returns>
        public string ToHexArgbBorder()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "#{0:X2}{1:X2}{2:X2}{3:X2}",
                BorderA, BorderR, BorderG, BorderB);
        }

        /// <summary>
        /// 画像塗り色を #AARRGGBB で返す。
        /// </summary>
        /// <returns>色文字列</returns>
        public string ToHexArgbFill()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "#{0:X2}{1:X2}{2:X2}{3:X2}",
                FillA, FillR, FillG, FillB);
        }

        /// <summary>
        /// ARGB を #AARRGGBB 文字列にする。
        /// </summary>
        /// <param name="a">A</param>
        /// <param name="r">R</param>
        /// <param name="g">G</param>
        /// <param name="b">B</param>
        /// <returns>色文字列</returns>
        public static string ToHexArgb(byte a, byte r, byte g, byte b)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "#{0:X2}{1:X2}{2:X2}{3:X2}",
                a, r, g, b);
        }
    }
}
