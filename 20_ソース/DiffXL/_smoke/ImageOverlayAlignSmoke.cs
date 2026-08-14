using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using DiffXL.LOGIC.Diff;

/// <summary>
/// ImageOverlayAligner のスモーク: 平行移動したペアを合わせる。
/// OpenCvSharp 直参照なし（DiffXL.exe のみ /r）。
/// </summary>
class Program
{
    static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "diffxl_overlay_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        int fail = 0;

        try
        {
            string left = Path.Combine(dir, "left.png");
            string right = Path.Combine(dir, "right.png");
            WritePair(left, right, shiftX: 18, shiftY: -11);

            ImageOverlayAlignResult r = ImageOverlayAligner.Align(left, right);
            Console.WriteLine(
                "method=" + r.Method
                + " aligned=" + r.Aligned
                + " shift=(" + r.ShiftX.ToString("0.###") + "," + r.ShiftY.ToString("0.###") + ")"
                + " conf=" + r.Confidence.ToString("0.####")
                + " size=" + r.Width + "x" + r.Height
                + " err=" + (r.ErrorMessage ?? ""));

            if (r.LeftPng == null || r.RightPng == null || r.LeftPng.Length == 0 || r.RightPng.Length == 0)
            {
                Console.WriteLine("FAIL missing png bytes");
                fail++;
            }

            if (!r.Aligned)
            {
                Console.WriteLine("FAIL expected aligned");
                fail++;
            }

            // 右を +18,-11 したので、合わせるシフトはおよそ -18,+11（右→左）
            if (Math.Abs(r.ShiftX + 18) > 2.5 || Math.Abs(r.ShiftY - 11) > 2.5)
            {
                Console.WriteLine("FAIL shift expected near (-18,11) got (" + r.ShiftX + "," + r.ShiftY + ")");
                fail++;
            }

            ImageOverlayAlignResult miss = ImageOverlayAligner.Align(left, Path.Combine(dir, "nope.png"));
            if (string.IsNullOrEmpty(miss.ErrorMessage))
            {
                Console.WriteLine("FAIL expected error for missing right");
                fail++;
            }
            else
            {
                Console.WriteLine("missing-right ok: " + miss.ErrorMessage);
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        Console.WriteLine(fail == 0 ? "PASS" : "FAIL count=" + fail);
        return fail == 0 ? 0 : 1;
    }

    static void WritePair(string leftPath, string rightPath, int shiftX, int shiftY)
    {
        int w = 160;
        int h = 120;
        using (var left = new Bitmap(w, h, PixelFormat.Format24bppRgb))
        using (var g = Graphics.FromImage(left))
        {
            g.Clear(Color.FromArgb(30, 30, 30));
            g.FillRectangle(Brushes.Orange, 40, 30, 50, 40);
            g.FillEllipse(Brushes.CornflowerBlue, 92, 62, 36, 36);
            g.DrawString("A", new Font("Arial", 18, FontStyle.Bold), Brushes.White, 18, 80);
            left.Save(leftPath, ImageFormat.Png);

            using (var right = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            using (var rg = Graphics.FromImage(right))
            {
                rg.Clear(Color.FromArgb(30, 30, 30));
                rg.DrawImage(left, shiftX, shiftY);
                // 小さな差分
                rg.FillRectangle(Brushes.Red, 70, 50, 12, 12);
                right.Save(rightPath, ImageFormat.Png);
            }
        }
    }
}
