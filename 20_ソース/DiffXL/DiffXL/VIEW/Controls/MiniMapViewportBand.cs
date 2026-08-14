using System;

namespace DiffXL.VIEW.Controls
{
    /// <summary>
    /// MiniMap 青帯の高さ・位置・ヒットの数式（WPF 非依存）。
    /// </summary>
    public static class MiniMapViewportBand
    {
        public const double MinHeightPx = 16;
        public const double LabelMinBandHeightPx = 22;

        public static double Clamp01(double value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 1)
            {
                return 1;
            }

            return value;
        }

        public static double VisibleFraction(double viewport, double extent)
        {
            if (viewport <= 0)
            {
                return 1;
            }

            if (extent <= viewport)
            {
                return 1;
            }

            return Clamp01(viewport / extent);
        }

        public static double BandHeight(double bodyH, double visibleFraction)
        {
            if (bodyH <= 0)
            {
                return 0;
            }

            double raw = Clamp01(visibleFraction) * bodyH;
            double h = Math.Max(MinHeightPx, raw);
            if (h > bodyH)
            {
                h = bodyH;
            }

            return h;
        }

        public static double BandTop(double bodyTop, double bodyH, double bandH, double ratio)
        {
            double travel = Math.Max(0, bodyH - bandH);
            return bodyTop + Clamp01(ratio) * travel;
        }

        public static bool HitTestThumb(double y, double bandTop, double bandH)
        {
            return y >= bandTop && y <= bandTop + bandH;
        }

        public static double RatioFromPointer(
            double pointerY,
            double grabOffset,
            double bodyTop,
            double bodyH,
            double bandH)
        {
            if (bodyH <= bandH)
            {
                return 0;
            }

            double travel = bodyH - bandH;
            return Clamp01((pointerY - grabOffset - bodyTop) / travel);
        }
    }
}
