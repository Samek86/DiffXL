using System;
using System.IO;
using System.Linq;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

/// <summary>
/// DiffEngine が ImageCorrespondenceService 経由で画像差分を出すことの回帰スモーク。
/// full_feature 製品カタログ:
///   Image >= 1 (IMG-B), ImageOnlyLeft == 1 (IMG-C), ImageOnlyRight == 1 (IMG-D),
///   Alignments.Count >= 1
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        string root = FindRepoRoot();
        string left = args.Length >= 1
            ? args[0]
            : Path.Combine(root, "30_参考資料", "samples", "full_feature_left.xlsx");
        string right = args.Length >= 2
            ? args[1]
            : Path.Combine(root, "30_参考資料", "samples", "full_feature_right.xlsx");

        Console.WriteLine("left=" + left);
        Console.WriteLine("right=" + right);
        if (!File.Exists(left) || !File.Exists(right))
        {
            Console.WriteLine("FAIL sample xlsx missing");
            return 2;
        }

        AppPaths.EnsureDirectories();
        NativeBootstrap.EnsureNativeBinaries();

        var engine = new DiffEngine();
        DiffResult result = engine.Compare(left, right);

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            Console.WriteLine("FAIL Error=" + result.ErrorMessage);
            return 1;
        }

        int image = result.Items.Count(i => i.Kind == DiffKind.Image);
        int onlyL = result.Items.Count(i => i.Kind == DiffKind.ImageOnlyLeft);
        int onlyR = result.Items.Count(i => i.Kind == DiffKind.ImageOnlyRight);
        int alignments = result.Alignments != null ? result.Alignments.Count : 0;

        Console.WriteLine("Items=" + result.Items.Count
            + " Image=" + image
            + " ImageOnlyLeft=" + onlyL
            + " ImageOnlyRight=" + onlyR
            + " Alignments=" + alignments
            + " ElapsedMs=" + (int)result.Elapsed.TotalMilliseconds);

        foreach (DiffItem i in result.Items.Where(x =>
            x.Kind == DiffKind.Image
            || x.Kind == DiffKind.ImageOnlyLeft
            || x.Kind == DiffKind.ImageOnlyRight))
        {
            Console.WriteLine("  [" + i.Kind + "] " + i.Summary
                + " sheetL=" + (i.SheetLeft ?? "")
                + " sheetR=" + (i.SheetRight ?? ""));
        }

        if (result.Alignments != null)
        {
            foreach (SheetAlignment a in result.Alignments)
            {
                int corr = a.Images != null ? a.Images.Count : 0;
                int exact = a.Images != null ? a.Images.Count(c => c.IsExactHashMatch) : 0;
                int paired = a.Images != null ? a.Images.Count(c => c.IsPaired && !c.IsExactHashMatch) : 0;
                int lo = a.Images != null ? a.Images.Count(c => c.IsLeftOnly) : 0;
                int ro = a.Images != null ? a.Images.Count(c => c.IsRightOnly) : 0;
                Console.WriteLine("  Alignment " + (a.LeftSheet ?? "?")
                    + " <-> " + (a.RightSheet ?? "?")
                    + " corr=" + corr
                    + " exact=" + exact
                    + " pairedDiff=" + paired
                    + " leftOnly=" + lo
                    + " rightOnly=" + ro
                    + " map=" + (a.ScrollMap != null));
            }
        }

        int fail = 0;
        if (image < 1)
        {
            Console.WriteLine("FAIL Image content diffs expected >= 1 (IMG-B) got " + image);
            fail++;
        }

        if (onlyL != 1)
        {
            Console.WriteLine("FAIL ImageOnlyLeft expected 1 (IMG-C) got " + onlyL);
            fail++;
        }

        if (onlyR != 1)
        {
            Console.WriteLine("FAIL ImageOnlyRight expected 1 (IMG-D) got " + onlyR);
            fail++;
        }

        if (alignments < 1)
        {
            Console.WriteLine("FAIL Alignments.Count expected >= 1 got " + alignments);
            fail++;
        }

        if (fail == 0)
        {
            Console.WriteLine("DIFFENGINE_ALIGNMENT_SMOKE_PASS");
            return 0;
        }

        Console.WriteLine("DIFFENGINE_ALIGNMENT_SMOKE_FAIL (" + fail + ")");
        return 1;
    }

    static string FindRepoRoot()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "30_参考資料", "samples")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir) != null
                ? Directory.GetParent(dir).FullName
                : null;
        }

        // _smoke → DiffXL → 20_ソース → repo
        dir = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
        if (Directory.Exists(Path.Combine(dir, "30_参考資料", "samples")))
        {
            return dir;
        }

        return Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
