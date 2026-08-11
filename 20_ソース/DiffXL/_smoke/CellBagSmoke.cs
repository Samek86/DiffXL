using System;
using System.Collections.Generic;
using System.Linq;
using DiffXL.LOGIC.Diff;

/// <summary>
/// CellBagComparer のスモーク（位置非依存の多重集合比較）。
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        int fail = 0;
        var pair = new SheetPair { LeftSheet = "Sheet1", RightSheet = "Sheet1" };

        // --- 1: A1 Hello / A2 Hello → 0 件 ---
        {
            var left = new[] { Cell("A1", 1, 1, "Hello", null) };
            var right = new[] { Cell("A2", 2, 1, "Hello", null) };
            IList<DiffItem> items = CellBagComparer.Compare(left, right, pair);
            Console.WriteLine("case1 same-text-diff-addr count=" + items.Count);
            if (items.Count != 0)
            {
                Console.WriteLine("FAIL case1 expected 0 diffs");
                Dump(items);
                fail++;
            }
            else
            {
                Console.WriteLine("OK case1");
            }
        }

        // --- 2: Hello 赤 / Hello 白 → Background 1 ---
        {
            var left = new[] { Cell("A1", 1, 1, "Hello", "#FFFF0000") };
            var right = new[] { Cell("B1", 1, 2, "Hello", "#FFFFFFFF") };
            IList<DiffItem> items = CellBagComparer.Compare(left, right, pair);
            Console.WriteLine("case2 bg-only count=" + items.Count);
            if (items.Count != 1 || items[0].Kind != DiffKind.Background)
            {
                Console.WriteLine("FAIL case2 expected 1 Background");
                Dump(items);
                fail++;
            }
            else if (items[0].BackgroundLeft != "#FFFF0000"
                     || items[0].BackgroundRight != "#FFFFFFFF")
            {
                Console.WriteLine("FAIL case2 BackgroundLeft/Right mismatch");
                Dump(items);
                fail++;
            }
            else
            {
                Console.WriteLine("OK case2 kind=Background bgL=" + items[0].BackgroundLeft
                    + " bgR=" + items[0].BackgroundRight);
            }
        }

        // --- 3: Hello,World / World,Hello → 0 件 ---
        {
            var left = new[]
            {
                Cell("A1", 1, 1, "Hello", null),
                Cell("A2", 2, 1, "World", null)
            };
            var right = new[]
            {
                Cell("B1", 1, 2, "World", null),
                Cell("B2", 2, 2, "Hello", null)
            };
            IList<DiffItem> items = CellBagComparer.Compare(left, right, pair);
            Console.WriteLine("case3 order-swap count=" + items.Count);
            if (items.Count != 0)
            {
                Console.WriteLine("FAIL case3 expected 0 diffs");
                Dump(items);
                fail++;
            }
            else
            {
                Console.WriteLine("OK case3");
            }
        }

        // --- 4: Hello×2 / Hello×1 → Text 片側 1 ---
        {
            var left = new[]
            {
                Cell("A1", 1, 1, "Hello", null),
                Cell("A2", 2, 1, "Hello", null)
            };
            var right = new[]
            {
                Cell("B1", 1, 2, "Hello", null)
            };
            IList<DiffItem> items = CellBagComparer.Compare(left, right, pair);
            Console.WriteLine("case4 count-mismatch count=" + items.Count);
            if (items.Count != 1 || items[0].Kind != DiffKind.Text)
            {
                Console.WriteLine("FAIL case4 expected 1 Text one-sided");
                Dump(items);
                fail++;
            }
            else if (string.IsNullOrEmpty(items[0].AddressLeft)
                     || !string.IsNullOrEmpty(items[0].AddressRight))
            {
                // 余りは左のみ
                Console.WriteLine("FAIL case4 expected left-only AddressLeft set, AddressRight null");
                Dump(items);
                fail++;
            }
            else
            {
                Console.WriteLine("OK case4 kind=Text left-only addr=" + items[0].AddressLeft);
            }
        }

        if (fail == 0)
        {
            Console.WriteLine("PASS CellBagSmoke");
            return 0;
        }

        Console.WriteLine("FAIL CellBagSmoke failures=" + fail);
        return 1;
    }

    static CellContent Cell(string address, int row, int col, string text, string bg)
    {
        return new CellContent
        {
            Address = address,
            Row = row,
            Column = col,
            Text = text,
            BackgroundArgb = bg,
            HasAnyBorder = false
        };
    }

    static void Dump(IList<DiffItem> items)
    {
        foreach (DiffItem d in items)
        {
            Console.WriteLine(string.Format(
                "  {0} L={1} R={2} bgL={3} bgR={4} | {5}",
                d.Kind,
                d.AddressLeft,
                d.AddressRight,
                d.BackgroundLeft,
                d.BackgroundRight,
                d.Summary));
        }
    }
}
