using System;
using DiffXL.VIEW.Controls;

internal static class MiniMapViewportBandSmoke
{
    private static int _fails;

    private static void Expect(bool cond, string name)
    {
        if (cond)
        {
            Console.WriteLine("OK " + name);
        }
        else
        {
            Console.WriteLine("FAIL " + name);
            _fails++;
        }
    }

    private static void ExpectNear(double actual, double expected, string name)
    {
        Expect(Math.Abs(actual - expected) < 0.0001, name + " actual=" + actual);
    }

    private static int Main()
    {
        Console.WriteLine("MiniMapViewportBandSmoke");

        ExpectNear(MiniMapViewportBand.VisibleFraction(400, 400), 1, "fit-exact");
        ExpectNear(MiniMapViewportBand.VisibleFraction(500, 400), 1, "fit-larger-viewport");
        ExpectNear(MiniMapViewportBand.VisibleFraction(200, 400), 0.5, "half");
        ExpectNear(MiniMapViewportBand.VisibleFraction(0, 400), 1, "viewport-zero");
        ExpectNear(MiniMapViewportBand.VisibleFraction(20, 4000), 0.005, "tiny-fraction");

        ExpectNear(MiniMapViewportBand.BandHeight(400, 1), 400, "band-full");
        ExpectNear(MiniMapViewportBand.BandHeight(400, 0.5), 200, "band-half");
        ExpectNear(MiniMapViewportBand.BandHeight(400, 0.005), 16, "band-min-16");
        ExpectNear(MiniMapViewportBand.BandHeight(10, 0.005), 10, "band-cap-body");

        ExpectNear(MiniMapViewportBand.BandTop(0, 400, 80, 0), 0, "top-ratio0");
        ExpectNear(MiniMapViewportBand.BandTop(0, 400, 80, 1), 320, "top-ratio1");
        ExpectNear(MiniMapViewportBand.BandTop(0, 400, 400, 0.7), 0, "no-travel");

        Expect(MiniMapViewportBand.HitTestThumb(10, 10, 16), "hit-top-edge");
        Expect(MiniMapViewportBand.HitTestThumb(26, 10, 16), "hit-bottom-edge");
        Expect(!MiniMapViewportBand.HitTestThumb(9.9, 10, 16), "miss-above");
        Expect(!MiniMapViewportBand.HitTestThumb(26.1, 10, 16), "miss-below");

        ExpectNear(MiniMapViewportBand.RatioFromPointer(40, 40, 0, 400, 80), 0, "grab-at-top-no-jump");
        ExpectNear(MiniMapViewportBand.RatioFromPointer(200, 40, 0, 400, 80), 0.5, "grab-mid");
        ExpectNear(MiniMapViewportBand.RatioFromPointer(200, 40, 0, 400, 400), 0, "no-scroll-ratio");
        ExpectNear(MiniMapViewportBand.RatioFromPointer(40, 8, 0, 400, 16), 0.0833333, "track-center-near-top");

        if (_fails > 0)
        {
            Console.WriteLine("FAILED " + _fails);
            return 1;
        }

        Console.WriteLine("ALL PASS");
        return 0;
    }
}
