using System;
using System.Collections.Generic;
using System.Linq;
using DiffXL.LOGIC.Diff;

/// <summary>
/// TableRowAligner / TableCompareService のスモーク。
/// 1) 12345 vs 1245 → TableRowDelete×1（"3"）、Insert 0
/// 2) 対応行内 1 セル変更 → TableCellChange
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        int fail = 0;
        var pair = new SheetPair { LeftSheet = "Sheet1", RightSheet = "Sheet1" };

        // --- 1: rows 1,2,3,4,5 vs 1,2,4,5 → Delete "3" ちょうど 1、Insert 0 ---
        {
            TableBlock left = Table("T1", new[] { "1", "2", "3", "4", "5" });
            TableBlock right = Table("T1", new[] { "1", "2", "4", "5" });

            // AlignRows 単体でも SkipLeft が "3" になること
            IList<AlignStep> steps = TableRowAligner.AlignRows(left.Rows, right.Rows);
            Console.WriteLine("case1 AlignRows steps=" + steps.Count);
            for (int k = 0; k < steps.Count; k++)
            {
                AlignStep s = steps[k];
                Console.WriteLine(string.Format(
                    "  [{0}] {1} L={2} R={3}",
                    k, s.Op, s.LeftIndex, s.RightIndex));
            }

            int skipL = steps.Count(s => s.Op == AlignOp.SkipLeft);
            int skipR = steps.Count(s => s.Op == AlignOp.SkipRight);
            int match = steps.Count(s => s.Op == AlignOp.Match);
            if (skipL != 1 || skipR != 0 || match != 4)
            {
                Console.WriteLine("FAIL case1 AlignRows expected Match×4 SkipLeft×1 SkipRight×0");
                fail++;
            }
            else if (steps.All(s => s.Op != AlignOp.SkipLeft || s.LeftIndex != 2))
            {
                Console.WriteLine("FAIL case1 SkipLeft should be LeftIndex=2 (row \"3\")");
                fail++;
            }
            else
            {
                Console.WriteLine("OK case1 AlignRows Match×4 SkipLeft@2");
            }

            IList<DiffItem> items = TableCompareService.Compare(
                new[] { left },
                new[] { right },
                pair);

            int deletes = items.Count(i => i.Kind == DiffKind.TableRowDelete);
            int inserts = items.Count(i => i.Kind == DiffKind.TableRowInsert);
            int changes = items.Count(i => i.Kind == DiffKind.TableCellChange);

            Console.WriteLine(string.Format(
                "case1 Compare delete={0} insert={1} change={2} total={3}",
                deletes, inserts, changes, items.Count));
            Dump(items);

            if (deletes != 1 || inserts != 0)
            {
                Console.WriteLine("FAIL case1 expected exactly 1 TableRowDelete and 0 Insert");
                fail++;
            }
            else
            {
                DiffItem del = items.First(i => i.Kind == DiffKind.TableRowDelete);
                if (del.RowIndexLeft != 2)
                {
                    Console.WriteLine("FAIL case1 Delete RowIndexLeft expected 2 actual=" + del.RowIndexLeft);
                    fail++;
                }
                else if (del.Summary == null || del.Summary.IndexOf("3", StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine("FAIL case1 Delete summary should mention \"3\": " + del.Summary);
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case1 TableRowDelete for \"3\"");
                }
            }
        }

        // --- 2: 対応行内 1 セル変更 → TableCellChange ---
        {
            // 同じ 2 行構造。2 行目の 2 列目だけ文言が違う
            var left = new TableBlock
            {
                Id = "T2",
                OrderIndex = 0,
                Rows = new List<IList<CellContent>>
                {
                    Row(1, new[] { "A", "B" }),
                    Row(2, new[] { "Hello", "World" })
                }
            };
            var right = new TableBlock
            {
                Id = "T2",
                OrderIndex = 0,
                Rows = new List<IList<CellContent>>
                {
                    Row(1, new[] { "A", "B" }),
                    Row(2, new[] { "Hello", "Changed" })
                }
            };

            IList<DiffItem> items = TableCompareService.Compare(
                new[] { left },
                new[] { right },
                pair);

            Console.WriteLine("case2 cell-change count=" + items.Count);
            Dump(items);

            int deletes = items.Count(i => i.Kind == DiffKind.TableRowDelete);
            int inserts = items.Count(i => i.Kind == DiffKind.TableRowInsert);
            int changes = items.Count(i => i.Kind == DiffKind.TableCellChange);

            if (deletes != 0 || inserts != 0 || changes != 1)
            {
                Console.WriteLine("FAIL case2 expected 0 delete/insert and 1 TableCellChange");
                fail++;
            }
            else
            {
                DiffItem ch = items[0];
                if (ch.Kind != DiffKind.TableCellChange)
                {
                    Console.WriteLine("FAIL case2 kind=" + ch.Kind);
                    fail++;
                }
                else if (ch.RowIndexLeft != 1 || ch.RowIndexRight != 1)
                {
                    Console.WriteLine("FAIL case2 RowIndex expected 1/1");
                    fail++;
                }
                else if (ch.Summary == null
                         || ch.Summary.IndexOf("Changed", StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine("FAIL case2 summary should mention Changed: " + ch.Summary);
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case2 TableCellChange World→Changed");
                }
            }
        }

        // --- 3: Bg のみ差でも TableCellChange ---
        {
            var left = new TableBlock
            {
                Id = "T3",
                Rows = new List<IList<CellContent>>
                {
                    new List<CellContent>
                    {
                        Cell("A1", 1, 1, "Same", "#FFFF0000")
                    }
                }
            };
            var right = new TableBlock
            {
                Id = "T3",
                Rows = new List<IList<CellContent>>
                {
                    new List<CellContent>
                    {
                        Cell("A1", 1, 1, "Same", "#FFFFFFFF")
                    }
                }
            };

            IList<DiffItem> items = TableCompareService.Compare(
                new[] { left },
                new[] { right },
                pair);

            if (items.Count != 1 || items[0].Kind != DiffKind.TableCellChange)
            {
                Console.WriteLine("FAIL case3 expected 1 TableCellChange for bg-only");
                Dump(items);
                fail++;
            }
            else if (items[0].BackgroundLeft != "#FFFF0000"
                     || items[0].BackgroundRight != "#FFFFFFFF")
            {
                Console.WriteLine("FAIL case3 BackgroundLeft/Right");
                Dump(items);
                fail++;
            }
            else
            {
                Console.WriteLine("OK case3 bg-only TableCellChange");
            }
        }

        if (fail == 0)
        {
            Console.WriteLine("PASS TableRowDiffSmoke");
            return 0;
        }

        Console.WriteLine("FAIL TableRowDiffSmoke failures=" + fail);
        return 1;
    }

    static TableBlock Table(string id, string[] rowTexts)
    {
        var rows = new List<IList<CellContent>>();
        for (int i = 0; i < rowTexts.Length; i++)
        {
            rows.Add(Row(i + 1, new[] { rowTexts[i] }));
        }

        return new TableBlock
        {
            Id = id,
            OrderIndex = 0,
            RowStart = 1,
            RowEnd = rowTexts.Length,
            ColStart = 1,
            ColEnd = 1,
            Rows = rows
        };
    }

    static List<CellContent> Row(int rowNum, string[] texts)
    {
        var cells = new List<CellContent>();
        for (int c = 0; c < texts.Length; c++)
        {
            cells.Add(Cell(
                Address(rowNum, c + 1),
                rowNum,
                c + 1,
                texts[c],
                null));
        }

        return cells;
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
            HasAnyBorder = true
        };
    }

    static string Address(int row, int col)
    {
        // 簡易 A1（列 1..26）
        char colLetter = (char)('A' + col - 1);
        return colLetter.ToString() + row.ToString();
    }

    static void Dump(IList<DiffItem> items)
    {
        foreach (DiffItem d in items)
        {
            Console.WriteLine(string.Format(
                "  {0} rowL={1} rowR={2} | {3}",
                d.Kind,
                d.RowIndexLeft.HasValue ? d.RowIndexLeft.Value.ToString() : "-",
                d.RowIndexRight.HasValue ? d.RowIndexRight.Value.ToString() : "-",
                d.Summary));
        }
    }
}
