using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

/// <summary>
/// DiffEngine 内容ベースパイプラインの必須シナリオスモーク（設計書 §7.1）。
/// exit code ≠ 0 で失敗。Excel COM 不要。
/// 既定: 30_参考資料/samples/content_diff_{left,right}.xlsx
/// 引数: leftPath rightPath
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        string repoSamples = ResolveSamplesDir();
        string left = args != null && args.Length >= 1
            ? args[0]
            : Path.Combine(repoSamples, "content_diff_left.xlsx");
        string right = args != null && args.Length >= 2
            ? args[1]
            : Path.Combine(repoSamples, "content_diff_right.xlsx");

        Console.WriteLine("left=" + left);
        Console.WriteLine("right=" + right);

        if (!File.Exists(left) || !File.Exists(right))
        {
            Console.WriteLine("FAIL sample xlsx missing. Run create_content_diff_samples.py first.");
            return 2;
        }

        AppPaths.EnsureDirectories();
        NativeBootstrap.EnsureNativeBinaries();

        var engine = new DiffEngine();
        DiffResult result = engine.Compare(left, right);

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            Console.WriteLine("FAIL DiffEngine error: " + result.ErrorMessage);
            return 1;
        }

        Console.WriteLine(string.Format(
            "COMPARE_OK items={0} elapsedMs={1} pairs={2}",
            result.Items.Count,
            (int)result.Elapsed.TotalMilliseconds,
            result.SheetPairs != null ? result.SheetPairs.Count : 0));

        int fail = 0;

        // LeftContent / RightContent 必須
        if (result.LeftContent == null || result.RightContent == null
            || result.LeftContent.Sheets == null || result.RightContent.Sheets == null
            || result.LeftContent.Sheets.Count == 0 || result.RightContent.Sheets.Count == 0)
        {
            Console.WriteLine("FAIL LeftContent/RightContent not set");
            fail++;
        }
        else
        {
            Console.WriteLine(string.Format(
                "OK content sheets L={0} R={1}",
                result.LeftContent.Sheets.Count,
                result.RightContent.Sheets.Count));
        }

        // 旧 Alignment / ScrollMap は生成しない（空可）
        int alignCount = result.Alignments != null ? result.Alignments.Count : 0;
        if (alignCount != 0)
        {
            Console.WriteLine("WARN Alignments.Count=" + alignCount + " (expected 0 empty)");
            // 非致命: 空が仕様。残っていてもスモークは通す場合あり → ここでは fail にしない
        }
        else
        {
            Console.WriteLine("OK Alignments empty (no Excel scroll map)");
        }

        DumpItems(result.Items);

        // --- 1: 位置無視 Hello (S_Cells) ---
        {
            List<DiffItem> sheetItems = OnSheet(result.Items, "S_Cells");
            int text = sheetItems.Count(i => i.Kind == DiffKind.Text);
            // タイトル行 "position-agnostic Hello" は左右同位置なので差なし。Hello も差なし。
            if (text != 0)
            {
                Console.WriteLine("FAIL case1 S_Cells expected 0 Text diffs, got " + text);
                fail++;
            }
            else
            {
                Console.WriteLine("OK case1 position-agnostic Hello (0 Text on S_Cells)");
            }
        }

        // --- 2: 背景差 (S_Bg) ---
        {
            List<DiffItem> sheetItems = OnSheet(result.Items, "S_Bg");
            int bg = sheetItems.Count(i => i.Kind == DiffKind.Background);
            if (bg < 1)
            {
                Console.WriteLine("FAIL case2 S_Bg expected >=1 Background, got " + bg);
                Dump(sheetItems);
                fail++;
            }
            else
            {
                Console.WriteLine("OK case2 background diff Background×" + bg);
            }
        }

        // --- 3: 表 12345 vs 1245 (S_TableDel) ---
        {
            List<DiffItem> sheetItems = OnSheet(result.Items, "S_TableDel");
            int del = sheetItems.Count(i => i.Kind == DiffKind.TableRowDelete);
            int ins = sheetItems.Count(i => i.Kind == DiffKind.TableRowInsert);
            if (del != 1 || ins != 0)
            {
                Console.WriteLine(string.Format(
                    "FAIL case3 S_TableDel expected Delete×1 Insert×0 got D={0} I={1}",
                    del, ins));
                Dump(sheetItems);
                fail++;
            }
            else
            {
                DiffItem d = sheetItems.First(i => i.Kind == DiffKind.TableRowDelete);
                string sum = d.Summary ?? string.Empty;
                if (sum.IndexOf("3", StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine("FAIL case3 Delete summary should mention 3: " + sum);
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case3 table 12345 vs 1245 TableRowDelete for 3");
                }
            }
        }

        // --- 4: 表セル変更 (S_TableCell) ---
        {
            List<DiffItem> sheetItems = OnSheet(result.Items, "S_TableCell");
            int ch = sheetItems.Count(i => i.Kind == DiffKind.TableCellChange);
            if (ch < 1)
            {
                Console.WriteLine("FAIL case4 S_TableCell expected >=1 TableCellChange, got " + ch);
                Dump(sheetItems);
                fail++;
            }
            else
            {
                DiffItem c = sheetItems.First(i => i.Kind == DiffKind.TableCellChange);
                string sum = c.Summary ?? string.Empty;
                if (sum.IndexOf("Changed", StringComparison.Ordinal) < 0
                    && sum.IndexOf("World", StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine("WARN case4 summary may not mention World/Changed: " + sum);
                }

                Console.WriteLine("OK case4 table cell change TableCellChange×" + ch);
            }
        }

        // --- 5: 画像同見た目異位置 (S_ImgSame) ---
        {
            List<DiffItem> sheetItems = OnSheet(result.Items, "S_ImgSame");
            int img = sheetItems.Count(i =>
                i.Kind == DiffKind.Image
                || i.Kind == DiffKind.ImageOnlyLeft
                || i.Kind == DiffKind.ImageOnlyRight);
            if (img != 0)
            {
                Console.WriteLine("FAIL case5 S_ImgSame expected 0 image diffs, got " + img);
                Dump(sheetItems);
                fail++;
            }
            else
            {
                // Content に画像が載っていること
                int li = CountImages(result.LeftContent, "S_ImgSame");
                int ri = CountImages(result.RightContent, "S_ImgSame");
                if (li < 1 || ri < 1)
                {
                    Console.WriteLine(string.Format(
                        "FAIL case5 images not extracted L={0} R={1}", li, ri));
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case5 same visual different position (0 image diffs)");
                }
            }
        }

        // --- 6: 画像 8 vs 9 (S_Img8v9) ---
        {
            List<DiffItem> sheetItems = OnSheet(result.Items, "S_Img8v9");
            int onlyR = sheetItems.Count(i => i.Kind == DiffKind.ImageOnlyRight);
            int onlyL = sheetItems.Count(i => i.Kind == DiffKind.ImageOnlyLeft);
            int li = CountImages(result.LeftContent, "S_Img8v9");
            int ri = CountImages(result.RightContent, "S_Img8v9");
            Console.WriteLine(string.Format(
                "case6 images L={0} R={1} OnlyRight={2} OnlyLeft={3} Image={4}",
                li, ri, onlyR, onlyL,
                sheetItems.Count(i => i.Kind == DiffKind.Image)));

            if (li != 8 || ri != 9)
            {
                Console.WriteLine(string.Format(
                    "FAIL case6 expected image counts 8 vs 9 got {0} vs {1}", li, ri));
                fail++;
            }
            else if (onlyR != 1 || onlyL != 0)
            {
                Console.WriteLine(string.Format(
                    "FAIL case6 expected ImageOnlyRight×1 OnlyLeft×0 got R={0} L={1}",
                    onlyR, onlyL));
                Dump(sheetItems);
                fail++;
            }
            else
            {
                Console.WriteLine("OK case6 image 8 vs 9 ImageOnlyRight×1 (resynced)");
            }
        }

        // --- 7: 画像部分差 → Regions (S_ImgPartial) ---
        {
            List<DiffItem> sheetItems = OnSheet(result.Items, "S_ImgPartial");
            List<DiffItem> images = sheetItems.Where(i => i.Kind == DiffKind.Image).ToList();
            if (images.Count < 1)
            {
                Console.WriteLine("FAIL case7 S_ImgPartial expected >=1 Image diff");
                Dump(sheetItems);
                fail++;
            }
            else
            {
                DiffItem img = images[0];
                int regions = img.HighlightRegions != null ? img.HighlightRegions.Count : 0;
                if (regions < 1)
                {
                    Console.WriteLine("FAIL case7 expected HighlightRegions >=1 got " + regions);
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case7 image partial regions=" + regions);
                }
            }
        }

        // --- 8: シート片側・同名 (S_LeftOnly Structure + S_Common ペア) ---
        {
            int structureLeft = result.Items.Count(i =>
                i.Kind == DiffKind.Structure
                && string.Equals(i.SheetLeft, "S_LeftOnly", StringComparison.OrdinalIgnoreCase));
            if (structureLeft < 1)
            {
                Console.WriteLine("FAIL case8 expected Structure for S_LeftOnly");
                fail++;
            }
            else
            {
                Console.WriteLine("OK case8 left-only sheet Structure");
            }

            bool hasCommonPair = result.SheetPairs != null
                && result.SheetPairs.Any(p =>
                    p != null
                    && string.Equals(p.LeftSheet, "S_Common", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.RightSheet, "S_Common", StringComparison.OrdinalIgnoreCase));
            if (!hasCommonPair)
            {
                Console.WriteLine("FAIL case8 expected same-name pair S_Common");
                fail++;
            }
            else
            {
                Console.WriteLine("OK case8 same-name sheet matching S_Common");
            }

            List<DiffItem> commonItems = OnSheet(result.Items, "S_Common");
            // タイトル以外の差分が無いこと（同一内容）
            int material = commonItems.Count(i =>
                i.Kind != DiffKind.Structure);
            if (material != 0)
            {
                Console.WriteLine("WARN case8 S_Common has " + material + " diffs (expected 0)");
                Dump(commonItems);
                // 同一内容のはずなので fail
                fail++;
            }
            else
            {
                Console.WriteLine("OK case8 S_Common no content diffs");
            }
        }

        if (fail == 0)
        {
            Console.WriteLine("PASS ContentDiffSmoke");
            return 0;
        }

        Console.WriteLine("FAIL ContentDiffSmoke fail=" + fail);
        return 1;
    }

    static string ResolveSamplesDir()
    {
        // _smoke → DiffXL → 20_ソース → repo
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\30_参考資料\samples")),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\30_参考資料\samples")),
            @"C:\JUN\WORK\DiffXL\30_参考資料\samples",
        };
        foreach (string c in candidates)
        {
            if (Directory.Exists(c))
            {
                return c;
            }
        }

        return candidates[candidates.Length - 1];
    }

    static List<DiffItem> OnSheet(IList<DiffItem> items, string sheet)
    {
        if (items == null)
        {
            return new List<DiffItem>();
        }

        return items.Where(i =>
            i != null
            && (string.Equals(i.SheetLeft, sheet, StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.SheetRight, sheet, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    static int CountImages(WorkbookContent wb, string sheet)
    {
        if (wb == null || wb.Sheets == null)
        {
            return 0;
        }

        SheetContent s = wb.Sheets.FirstOrDefault(x =>
            x != null && string.Equals(x.Name, sheet, StringComparison.OrdinalIgnoreCase));
        if (s == null || s.Images == null)
        {
            return 0;
        }

        return s.Images.Count;
    }

    static void DumpItems(IList<DiffItem> items)
    {
        if (items == null)
        {
            return;
        }

        Console.WriteLine("--- all items ---");
        foreach (DiffItem i in items)
        {
            Console.WriteLine(string.Format(
                "  [{0}] L={1} R={2} regions={3} | {4}",
                i.Kind,
                i.SheetLeft ?? "-",
                i.SheetRight ?? "-",
                i.HighlightRegions != null ? i.HighlightRegions.Count : 0,
                i.Summary));
        }
    }

    static void Dump(IList<DiffItem> items)
    {
        foreach (DiffItem i in items)
        {
            Console.WriteLine("    " + i.Kind + " | " + i.Summary);
        }
    }
}
