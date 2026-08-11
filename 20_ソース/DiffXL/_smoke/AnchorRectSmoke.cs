using System;
using System.IO;
using System.Linq;
using DiffXL.LOGIC.Diff;

/// <summary>
/// 画像アンカー矩形（from〜to）抽出のスモーク。
/// full_feature_left.xlsx の「製品カタログ」シートを対象とする。
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        string leftPath = args != null && args.Length > 0
            ? args[0]
            : @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_left.xlsx";

        if (!File.Exists(leftPath))
        {
            Console.WriteLine("FAIL file not found: " + leftPath);
            return 2;
        }

        string cache = Path.Combine(Path.GetTempPath(), "diffxl_anchor_rect_smoke_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(cache);
        int fail = 0;

        try
        {
            using (var r = XlsxPackageReader.Open(leftPath))
            {
                var imgs = r.ExtractImages("製品カタログ", cache);
                Console.WriteLine("images=" + imgs.Count + " sheet=製品カタログ");
                if (imgs.Count == 0)
                {
                    Console.WriteLine("FAIL no images extracted for 製品カタログ");
                    return 1;
                }

                bool anySpan = false;
                foreach (var i in imgs)
                {
                    string a = i.Anchor == null
                        ? "null"
                        : string.Format("R{0}-{1} C{2}-{3}", i.Anchor.RowStart, i.Anchor.RowEnd, i.Anchor.ColStart, i.Anchor.ColEnd);
                    Console.WriteLine("  " + (i.FileName ?? "?")
                        + " AnchorRow=" + i.AnchorRow
                        + " AnchorCol=" + i.AnchorColumn
                        + " Anchor=" + a
                        + " " + i.PixelWidth + "x" + i.PixelHeight);

                    if (i.Anchor == null)
                    {
                        Console.WriteLine("FAIL Anchor is null: " + i.FileName);
                        fail++;
                        continue;
                    }

                    if (i.Anchor.RowStart < 1)
                    {
                        Console.WriteLine("FAIL RowStart < 1: " + i.FileName + " RowStart=" + i.Anchor.RowStart);
                        fail++;
                    }

                    if (i.Anchor.RowEnd < i.Anchor.RowStart)
                    {
                        Console.WriteLine("FAIL RowEnd < RowStart: " + i.FileName
                            + " " + i.Anchor.RowEnd + " < " + i.Anchor.RowStart);
                        fail++;
                    }

                    if (i.Anchor.ColStart < 1)
                    {
                        Console.WriteLine("FAIL ColStart < 1: " + i.FileName + " ColStart=" + i.Anchor.ColStart);
                        fail++;
                    }

                    if (i.Anchor.ColEnd < i.Anchor.ColStart)
                    {
                        Console.WriteLine("FAIL ColEnd < ColStart: " + i.FileName
                            + " " + i.Anchor.ColEnd + " < " + i.Anchor.ColStart);
                        fail++;
                    }

                    // 互換: AnchorRow/Column は Start と同期
                    if (i.AnchorRow != i.Anchor.RowStart || i.AnchorColumn != i.Anchor.ColStart)
                    {
                        Console.WriteLine("FAIL AnchorRow/Column not synced: "
                            + i.FileName
                            + " AnchorRow=" + i.AnchorRow + " vs RowStart=" + i.Anchor.RowStart
                            + " AnchorColumn=" + i.AnchorColumn + " vs ColStart=" + i.Anchor.ColStart);
                        fail++;
                    }

                    if (i.Anchor.RowEnd > i.Anchor.RowStart || i.Anchor.ColEnd > i.Anchor.ColStart)
                    {
                        anySpan = true;
                    }
                }

                if (!anySpan)
                {
                    Console.WriteLine("FAIL expected at least one twoCell span (RowEnd>RowStart or ColEnd>ColStart)");
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK at least one image spans multiple cells");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL exception: " + ex);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(cache))
                {
                    Directory.Delete(cache, true);
                }
            }
            catch
            {
                // ignore cleanup
            }
        }

        if (fail == 0)
        {
            Console.WriteLine("ANCHOR_RECT_SMOKE_PASS");
            return 0;
        }

        Console.WriteLine("ANCHOR_RECT_SMOKE_FAIL count=" + fail);
        return 1;
    }
}
