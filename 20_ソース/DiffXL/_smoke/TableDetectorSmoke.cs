using System;
using System.Collections.Generic;
using System.Linq;
using DiffXL.LOGIC.Diff;

/// <summary>
/// TableDetector のスモーク。
/// 3x3 ボーダー格子 + 外の Hello → Tables.Count==1, LooseCells に Hello。
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        int fail = 0;

        // --- ケース1: 3x3 border + 外に Hello ---
        var cells = new List<CellContent>();
        for (int r = 1; r <= 3; r++)
        {
            for (int c = 1; c <= 3; c++)
            {
                cells.Add(new CellContent
                {
                    Address = Addr(r, c),
                    Row = r,
                    Column = c,
                    Text = "T" + r + c,
                    BackgroundArgb = null,
                    HasAnyBorder = true
                });
            }
        }

        cells.Add(new CellContent
        {
            Address = "E5",
            Row = 5,
            Column = 5,
            Text = "Hello",
            BackgroundArgb = null,
            HasAnyBorder = false
        });

        TableDetectResult result = TableDetector.Detect(cells);
        Console.WriteLine("case1 Tables=" + result.Tables.Count
            + " Loose=" + result.LooseCells.Count);

        if (result.Tables.Count != 1)
        {
            Console.WriteLine("FAIL case1 Tables.Count expected=1 actual=" + result.Tables.Count);
            fail++;
        }
        else
        {
            TableBlock t = result.Tables[0];
            Console.WriteLine(string.Format(
                "  table Id={0} Order={1} R{2}:{3} C{4}:{5} Rows={6}x{7}",
                t.Id,
                t.OrderIndex,
                t.RowStart,
                t.RowEnd,
                t.ColStart,
                t.ColEnd,
                t.Rows != null ? t.Rows.Count : 0,
                t.Rows != null && t.Rows.Count > 0 ? t.Rows[0].Count : 0));

            if (t.RowStart != 1 || t.RowEnd != 3 || t.ColStart != 1 || t.ColEnd != 3)
            {
                Console.WriteLine("FAIL case1 bounds expected R1:3 C1:3");
                fail++;
            }

            if (t.OrderIndex != 0)
            {
                Console.WriteLine("FAIL case1 OrderIndex expected=0 actual=" + t.OrderIndex);
                fail++;
            }

            if (t.Rows == null || t.Rows.Count != 3 || t.Rows.Any(row => row == null || row.Count != 3))
            {
                Console.WriteLine("FAIL case1 Rows expected 3x3 matrix");
                fail++;
            }
            else if (t.Rows[0][0].Text != "T11" || t.Rows[2][2].Text != "T33")
            {
                Console.WriteLine("FAIL case1 corner texts T11/T33");
                fail++;
            }
            else
            {
                Console.WriteLine("OK case1 table matrix");
            }
        }

        if (!result.LooseCells.Any(c => c.Text == "Hello"))
        {
            Console.WriteLine("FAIL case1 LooseCells missing Hello");
            fail++;
            foreach (CellContent c in result.LooseCells)
            {
                Console.WriteLine("  loose " + c.Address + " Text=" + (c.Text ?? "(null)"));
            }
        }
        else
        {
            Console.WriteLine("OK case1 LooseCells has Hello");
        }

        if (result.LooseCells.Count != 1)
        {
            Console.WriteLine("FAIL case1 LooseCells.Count expected=1 actual=" + result.LooseCells.Count);
            fail++;
        }

        // --- ケース2: 孤立ボーダー 1 セルは表にしない ---
        var single = new List<CellContent>
        {
            new CellContent
            {
                Address = "A1",
                Row = 1,
                Column = 1,
                Text = "Solo",
                HasAnyBorder = true
            },
            new CellContent
            {
                Address = "B2",
                Row = 2,
                Column = 2,
                Text = "Loose2",
                HasAnyBorder = false
            }
        };
        TableDetectResult r2 = TableDetector.Detect(single);
        Console.WriteLine("case2 Tables=" + r2.Tables.Count + " Loose=" + r2.LooseCells.Count);
        if (r2.Tables.Count != 0)
        {
            Console.WriteLine("FAIL case2 expected no tables for 1x1 border");
            fail++;
        }
        else if (r2.LooseCells.Count != 2
            || !r2.LooseCells.Any(c => c.Text == "Solo")
            || !r2.LooseCells.Any(c => c.Text == "Loose2"))
        {
            Console.WriteLine("FAIL case2 expected Solo+Loose2 in LooseCells");
            fail++;
        }
        else
        {
            Console.WriteLine("OK case2 no table, both loose");
        }

        // --- ケース3: null / 空 ---
        TableDetectResult r3 = TableDetector.Detect(null);
        TableDetectResult r4 = TableDetector.Detect(new List<CellContent>());
        if (r3.Tables.Count != 0 || r3.LooseCells.Count != 0
            || r4.Tables.Count != 0 || r4.LooseCells.Count != 0)
        {
            Console.WriteLine("FAIL case3 empty/null should yield empty result");
            fail++;
        }
        else
        {
            Console.WriteLine("OK case3 empty/null");
        }

        if (fail > 0)
        {
            Console.WriteLine("FAIL TableDetectorSmoke fails=" + fail);
            return 1;
        }

        Console.WriteLine("PASS TableDetectorSmoke");
        return 0;
    }

    static string Addr(int row, int col)
    {
        // スモーク用の簡易 A1（列 1..26 想定）
        return ((char)('A' + col - 1)).ToString() + row.ToString();
    }
}
