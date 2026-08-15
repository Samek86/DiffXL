using System;
using System.Collections.Generic;
using System.Linq;
using DiffXL.LOGIC.Diff;

/// <summary>
/// 表行類似度の真理値スモーク。
/// 1 セル一致や空欄パディングだけで Match しないこと、
/// 非空 2 セル一致なら Match することを固定する。
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        int fail = 0;

        // 2列: [1,A] vs [2,A] → Match ではない（Skip 対）
        {
            IList<IList<CellContent>> left = new List<IList<CellContent>> { Row(1, "1", "A") };
            IList<IList<CellContent>> right = new List<IList<CellContent>> { Row(1, "2", "A") };
            IList<AlignStep> steps = TableRowAligner.AlignRows(left, right);
            Dump("case1 [1,A] vs [2,A]", steps);

            bool hasMatch = steps.Any(s => s.Op == AlignOp.Match);
            int skipL = steps.Count(s => s.Op == AlignOp.SkipLeft);
            int skipR = steps.Count(s => s.Op == AlignOp.SkipRight);

            if (hasMatch)
            {
                Console.WriteLine("FAIL case1 expected no Match for one shared cell [1,A] vs [2,A]");
                fail++;
            }
            else if (skipL != 1 || skipR != 1)
            {
                Console.WriteLine("FAIL case1 expected SkipLeft×1 SkipRight×1");
                fail++;
            }
            else
            {
                Console.WriteLine("OK case1 no Match (Skip pair)");
            }
        }

        // 3列: [1,A,x] vs [1,A,y] → Match（非空 2 칸 일치）
        {
            IList<IList<CellContent>> left = new List<IList<CellContent>> { Row(1, "1", "A", "x") };
            IList<IList<CellContent>> right = new List<IList<CellContent>> { Row(1, "1", "A", "y") };
            IList<AlignStep> steps = TableRowAligner.AlignRows(left, right);
            Dump("case2 [1,A,x] vs [1,A,y]", steps);

            if (steps.Count != 1 || steps[0].Op != AlignOp.Match
                || steps[0].LeftIndex != 0 || steps[0].RightIndex != 0)
            {
                Console.WriteLine("FAIL case2 expected single Match for two shared non-empty cells");
                fail++;
            }
            else
            {
                Console.WriteLine("OK case2 Match on two non-empty equals");
            }
        }

        // 空欄パディング: 非空 1 칸만으로는 Match 금지
        // 同一キー [1,"",""] は exact-key 1.0 で Match（別ケース）。
        // ここは空欄が一致を膨らませる [1,"",""] vs [2,"",""] を禁じる。
        {
            IList<IList<CellContent>> left = new List<IList<CellContent>> { Row(1, "1", "", "") };
            IList<IList<CellContent>> right = new List<IList<CellContent>> { Row(1, "2", "", "") };
            IList<AlignStep> steps = TableRowAligner.AlignRows(left, right);
            Dump("case3 [1,\"\",\"\"] vs [2,\"\",\"\"]", steps);

            bool hasMatch = steps.Any(s => s.Op == AlignOp.Match);
            if (hasMatch)
            {
                Console.WriteLine("FAIL case3 blank padding must not Match when only 1 non-empty cell");
                fail++;
            }
            else
            {
                Console.WriteLine("OK case3 no Match on blank-padded single non-empty");
            }
        }

        // 同一キーの疎行は exact-key で Match のまま
        {
            IList<IList<CellContent>> left = new List<IList<CellContent>> { Row(1, "1", "", "") };
            IList<IList<CellContent>> right = new List<IList<CellContent>> { Row(1, "1", "", "") };
            IList<AlignStep> steps = TableRowAligner.AlignRows(left, right);
            Dump("case4 identical [1,\"\",\"\"]", steps);

            if (steps.Count != 1 || steps[0].Op != AlignOp.Match)
            {
                Console.WriteLine("FAIL case4 exact-key identical sparse row should Match");
                fail++;
            }
            else
            {
                Console.WriteLine("OK case4 exact-key sparse Match");
            }
        }

        // 余列: 左 A B C / 右 A B C NEW → 4 列目を TableCellChange として残す
        {
            var left = new TableBlock
            {
                Id = "Tx",
                OrderIndex = 0,
                Rows = new List<IList<CellContent>> { Row(1, "A", "B", "C") }
            };
            var right = new TableBlock
            {
                Id = "Tx",
                OrderIndex = 0,
                Rows = new List<IList<CellContent>> { Row(1, "A", "B", "C", "NEW") }
            };
            var pair = new SheetPair { LeftSheet = "Sheet1", RightSheet = "Sheet1" };

            IList<DiffItem> items = TableCompareService.Compare(
                new[] { left },
                new[] { right },
                pair);

            int changes = items.Count(i => i.Kind == DiffKind.TableCellChange);
            Console.WriteLine("case5 extra-col TableCellChange count=" + changes);
            foreach (DiffItem d in items)
            {
                Console.WriteLine(string.Format(
                    "  {0} addrL={1} addrR={2} | {3}",
                    d.Kind,
                    d.AddressLeft ?? "-",
                    d.AddressRight ?? "-",
                    d.Summary));
            }

            if (changes < 1)
            {
                Console.WriteLine("FAIL case5 expected >=1 TableCellChange for extra column NEW");
                fail++;
            }
            else
            {
                DiffItem ch = items.First(i => i.Kind == DiffKind.TableCellChange);
                bool addrOk = (ch.AddressRight != null
                               && ch.AddressRight.IndexOf("D", StringComparison.Ordinal) >= 0)
                              || (ch.AddressLeft != null
                                  && ch.AddressLeft.IndexOf("D", StringComparison.Ordinal) >= 0);
                bool summaryOk = ch.Summary != null
                                 && ch.Summary.IndexOf("NEW", StringComparison.Ordinal) >= 0;
                if (!addrOk && !summaryOk)
                {
                    Console.WriteLine(
                        "FAIL case5 Summary/Address should point at 4th col NEW: "
                        + ch.AddressLeft + "/" + ch.AddressRight + " " + ch.Summary);
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case5 extra column TableCellChange");
                }
            }
        }

        if (fail == 0)
        {
            Console.WriteLine("PASS TableTruthSmoke");
            return 0;
        }

        Console.WriteLine("FAIL TableTruthSmoke failures=" + fail);
        return 1;
    }

    static List<CellContent> Row(int rowNum, params string[] texts)
    {
        var cells = new List<CellContent>();
        for (int c = 0; c < texts.Length; c++)
        {
            cells.Add(new CellContent
            {
                Address = ((char)('A' + c)).ToString() + rowNum.ToString(),
                Row = rowNum,
                Column = c + 1,
                Text = texts[c],
                HasAnyBorder = true
            });
        }

        return cells;
    }

    static void Dump(string title, IList<AlignStep> steps)
    {
        Console.WriteLine(title + " steps=" + steps.Count);
        for (int k = 0; k < steps.Count; k++)
        {
            AlignStep s = steps[k];
            Console.WriteLine(string.Format(
                "  [{0}] {1} L={2} R={3}",
                k, s.Op, s.LeftIndex, s.RightIndex));
        }
    }
}
