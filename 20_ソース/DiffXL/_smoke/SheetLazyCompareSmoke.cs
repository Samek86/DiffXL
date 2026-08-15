using System;
using System.IO;
using System.Linq;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

internal static class SheetLazyCompareSmoke
{
    private static int _fails;

    private static void Expect(bool cond, string name)
    {
        Console.WriteLine((cond ? "OK " : "FAIL ") + name);
        if (!cond)
        {
            _fails++;
        }
    }

    private static int Main()
    {
        Console.WriteLine("SheetLazyCompareSmoke");
        string samples = ResolveSamplesDir();
        string left = Path.Combine(samples, "content_diff_left.xlsx");
        string right = Path.Combine(samples, "content_diff_right.xlsx");
        if (!File.Exists(left) || !File.Exists(right))
        {
            Console.WriteLine("FAIL samples missing");
            return 2;
        }

        AppPaths.EnsureDirectories();
        NativeBootstrap.EnsureNativeBinaries();

        var engine = new DiffEngine();
        DiffResult full = engine.Compare(left, right);
        Expect(full.Timings != null && full.Timings.TotalMs >= 0, "full timings");
        Expect(full.LeftContent != null && full.LeftContent.Sheets.Count >= 8, "full all sheet names");
        int fullPop = full.LeftContent.Sheets.Count(s => s != null && s.IsPopulated);
        Expect(fullPop >= 8, "full all populated got " + fullPop);

        var opts = new CompareOptions
        {
            LazySheets = true,
            FocusPair = new SheetPair { LeftSheet = "S_Cells", RightSheet = "S_Cells" }
        };
        DiffResult lazy = engine.Compare(left, right, opts);
        Expect(lazy.IsLazy, "lazy flag");
        Expect(lazy.Timings != null && lazy.Timings.ReadMs >= 0, "lazy read ms");
        Expect(lazy.LeftContent.Sheets.Count >= 8, "lazy keeps sheet name stubs");
        SheetContent cells = lazy.LeftContent.Sheets.FirstOrDefault(s => s != null && s.Name == "S_Cells");
        Expect(cells != null && cells.IsPopulated, "S_Cells populated");
        SheetContent bg = lazy.LeftContent.Sheets.FirstOrDefault(s => s != null && s.Name == "S_Bg");
        Expect(bg != null && !bg.IsPopulated, "S_Bg is stub");
        Expect(lazy.ComparedPairKeys != null
            && lazy.ComparedPairKeys.Contains(DiffEngine.MakePairKey("S_Cells", "S_Cells")),
            "compared key S_Cells");
        Expect(
            !lazy.Items.Any(i => i != null && i.Kind != DiffKind.Structure
                && string.Equals(i.SheetLeft, "S_Bg", StringComparison.OrdinalIgnoreCase)),
            "no S_Bg content diffs yet");

        engine.CompareSheetPair(
            lazy,
            left,
            right,
            new SheetPair { LeftSheet = "S_Bg", RightSheet = "S_Bg" },
            opts,
            null);
        SheetContent bg2 = lazy.LeftContent.Sheets.First(s => s.Name == "S_Bg");
        Expect(bg2.IsPopulated, "S_Bg populated after CompareSheetPair");
        Expect(lazy.Items.Any(i => i != null && i.Kind == DiffKind.Background
            && string.Equals(i.SheetLeft, "S_Bg", StringComparison.OrdinalIgnoreCase)),
            "S_Bg background diff after increment");

        Console.WriteLine(_fails == 0 ? "ALL PASS" : "FAILED " + _fails);
        return _fails == 0 ? 0 : 1;
    }

    private static string ResolveSamplesDir()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "30_参考資料", "samples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir) != null ? Directory.GetParent(dir).FullName : null;
        }

        return Path.Combine("30_参考資料", "samples");
    }
}
