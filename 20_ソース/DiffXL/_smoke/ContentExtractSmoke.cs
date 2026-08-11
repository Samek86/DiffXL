using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiffXL.LOGIC.Diff;

/// <summary>
/// セル背景色・ボーダー抽出（EnumerateCellContents）のスモーク。
/// content_extract_sample.xlsx または引数の xlsx を対象とする。
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        string sample = args != null && args.Length > 0
            ? args[0]
            : @"C:\JUN\WORK\DiffXL\30_参考資料\samples\content_extract_sample.xlsx";

        if (!File.Exists(sample))
        {
            Console.WriteLine("FAIL file not found: " + sample);
            return 2;
        }

        Console.WriteLine("file=" + sample);
        int fail = 0;

        try
        {
            using (var reader = XlsxPackageReader.Open(sample))
            {
                IReadOnlyList<string> sheets = reader.GetSheetNames();
                if (sheets == null || sheets.Count == 0)
                {
                    Console.WriteLine("FAIL no sheets");
                    return 1;
                }

                string sheet = sheets[0];
                Console.WriteLine("sheet=" + sheet);

                List<CellContent> cells = reader.EnumerateCellContents(sheet).ToList();
                Console.WriteLine("cells=" + cells.Count);
                foreach (CellContent c in cells.OrderBy(x => x.Row).ThenBy(x => x.Column))
                {
                    Console.WriteLine(string.Format(
                        "  {0} Text={1} Bg={2} Border={3} R={4} C={5}",
                        c.Address,
                        Quote(c.Text),
                        c.BackgroundArgb ?? "(null)",
                        c.HasAnyBorder,
                        c.Row,
                        c.Column));
                }

                // EnumerateCells 互換: Text が一致すること
                List<CellValue> legacy = reader.EnumerateCells(sheet).ToList();
                if (legacy.Count != cells.Count)
                {
                    Console.WriteLine("FAIL EnumerateCells count mismatch: " + legacy.Count + " vs " + cells.Count);
                    fail++;
                }
                else
                {
                    for (int i = 0; i < cells.Count; i++)
                    {
                        if (!string.Equals(legacy[i].Text, cells[i].Text, StringComparison.Ordinal)
                            || !string.Equals(legacy[i].Address, cells[i].Address, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("FAIL EnumerateCells wrapper mismatch at " + i);
                            fail++;
                            break;
                        }
                    }
                }

                Dictionary<string, CellContent> byAddr = cells.ToDictionary(
                    c => c.Address,
                    c => c,
                    StringComparer.OrdinalIgnoreCase);

                // 期待: A1 Hello + 赤背景
                fail += Expect(byAddr, "A1", "Hello", "#FFFF0000", false);
                // B2 Bordered + 青 + border
                fail += Expect(byAddr, "B2", "Bordered", "#FF0000FF", true);
                // C3 Plain + 背景なし + border なし
                fail += Expect(byAddr, "C3", "Plain", null, false);
                // D4 Yellow
                fail += Expect(byAddr, "D4", "Yellow", "#FFFFFF00", false);
                // E5 Edge + border only
                fail += Expect(byAddr, "E5", "Edge", null, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL exception: " + ex);
            return 1;
        }

        if (fail > 0)
        {
            Console.WriteLine("FAIL count=" + fail);
            return 1;
        }

        Console.WriteLine("PASS ContentExtractSmoke");
        return 0;
    }

    /// <summary>
    /// 期待値チェック。bgExpected が null のときは背景なしを要求。
    /// </summary>
    static int Expect(
        Dictionary<string, CellContent> byAddr,
        string addr,
        string text,
        string bgExpected,
        bool expectedBorder)
    {
        CellContent c;
        if (!byAddr.TryGetValue(addr, out c))
        {
            Console.WriteLine("FAIL missing cell " + addr);
            return 1;
        }

        int fail = 0;
        if (!string.Equals(c.Text, text, StringComparison.Ordinal))
        {
            Console.WriteLine("FAIL " + addr + " Text expected=" + text + " actual=" + Quote(c.Text));
            fail++;
        }

        if (bgExpected == null)
        {
            if (c.BackgroundArgb != null)
            {
                Console.WriteLine("FAIL " + addr + " Bg expected=null actual=" + c.BackgroundArgb);
                fail++;
            }
        }
        else if (!string.Equals(c.BackgroundArgb, bgExpected, StringComparison.OrdinalIgnoreCase))
        {
            // 6 桁相当の比較も許容（#FFRRGGBB の末尾 6）
            string exp6 = bgExpected.Length >= 7 ? bgExpected.Substring(bgExpected.Length - 6) : bgExpected;
            string act6 = c.BackgroundArgb != null && c.BackgroundArgb.Length >= 7
                ? c.BackgroundArgb.Substring(c.BackgroundArgb.Length - 6)
                : (c.BackgroundArgb ?? "");
            if (!string.Equals(exp6, act6, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("FAIL " + addr + " Bg expected=" + bgExpected + " actual=" + (c.BackgroundArgb ?? "(null)"));
                fail++;
            }
            else
            {
                Console.WriteLine("OK " + addr + " Bg match (RRGGBB) " + act6);
            }
        }
        else
        {
            Console.WriteLine("OK " + addr + " Bg=" + c.BackgroundArgb);
        }

        if (c.HasAnyBorder != expectedBorder)
        {
            Console.WriteLine("FAIL " + addr + " Border expected=" + expectedBorder + " actual=" + c.HasAnyBorder);
            fail++;
        }
        else
        {
            Console.WriteLine("OK " + addr + " Border=" + c.HasAnyBorder + " Text=" + Quote(c.Text));
        }

        return fail;
    }

    static string Quote(string s)
    {
        return "\"" + (s ?? "") + "\"";
    }
}
