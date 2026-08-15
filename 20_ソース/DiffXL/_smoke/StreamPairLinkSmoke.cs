// Stream pair link: DiffItem.StreamPairIndex via DiffResultLinker.Attach
// Compile against built DiffXL (see other smokes) or run via msbuild + csc.

using System;
using System.Collections.Generic;
using System.Linq;
using DiffXL.LOGIC.Diff;

internal static class StreamPairLinkSmoke
{
    private static int _fails;

    private static void Expect(bool cond, string name)
    {
        if (cond)
        {
            Console.WriteLine("OK " + name);
        }
        else
        {
            Console.WriteLine("FAIL " + name);
            _fails++;
        }
    }

    private static void Main()
    {
        Console.WriteLine("StreamPairLinkSmoke");

        // C: インデックスがなければ Attach 前 StreamPairIndex == -1
        var unset = new DiffItem { Kind = DiffKind.Text, AddressLeft = "A1" };
        Expect(unset.StreamPairIndex == -1, "C default StreamPairIndex -1");

        // A: 左右 loose セル Hello(A1) / Hello(Z9) — 1 Match、差分 0、unlinked 0
        {
            var left = new SheetContent
            {
                Name = "A",
                LooseCells = new List<CellContent> { Cell(1, 1, "Hello") }
            };
            var right = new SheetContent
            {
                Name = "A",
                LooseCells = new List<CellContent> { Cell(9, 26, "Hello") }
            };
            IList<ContentStreamPair> pairs = ContentStreamBuilder.Align(
                ContentStreamBuilder.Build(left),
                ContentStreamBuilder.Build(right));
            Expect(pairs.Count == 1 && pairs[0].Op == AlignOp.Match, "A one Match");
            var result = new DiffResult();
            DiffResultLinker.Attach(result, pairs);
            Expect(DiffResultLinker.CountUnlinkedContentItems(result) == 0, "A unlinked 0");
        }

        // B: 行番号が違っても近い検証メモ同士は Match（B17↔B19 相当）
        {
            var leftSheet = new SheetContent
            {
                Name = "SC",
                LooseCells = new List<CellContent>
                {
                    Cell(1, 1, "SC_テキスト挿入"),
                    Cell(17, 2, "検証: L10(S03)→R12 / R8 挿入区間は左ホールド(≤7)。")
                }
            };
            var rightSheet = new SheetContent
            {
                Name = "SC",
                LooseCells = new List<CellContent>
                {
                    Cell(1, 1, "SC_テキスト挿入"),
                    Cell(19, 2, "検証: 挿入行(R8-9)スクロール中は左が S02 付近でホールド。")
                }
            };
            IList<ContentStreamPair> pairs = ContentStreamBuilder.Align(
                ContentStreamBuilder.Build(leftSheet),
                ContentStreamBuilder.Build(rightSheet));
            Expect(pairs.Count == 2 && pairs.All(p => p.Op == AlignOp.Match), "B verify-notes paired");
            Expect(
                pairs.Any(p => p.Op == AlignOp.Match
                    && p.Left != null && p.Right != null
                    && p.Left.Row == 17 && p.Right.Row == 19),
                "B B17-style paired with B19-style");

            var leftNote = new DiffItem
            {
                Kind = DiffKind.Text,
                AddressLeft = "B17",
                Summary = "検証: L10"
            };
            var rightNote = new DiffItem
            {
                Kind = DiffKind.Text,
                AddressRight = "B19",
                Summary = "検証: 挿入行"
            };
            var result = new DiffResult();
            result.Items.Add(leftNote);
            result.Items.Add(rightNote);

            Expect(leftNote.StreamPairIndex == -1 && rightNote.StreamPairIndex == -1, "B pre-attach -1");
            DiffResultLinker.Attach(result, pairs);
            Expect(leftNote.StreamPairIndex == rightNote.StreamPairIndex
                && leftNote.StreamPairIndex >= 0, "same pair");
            Expect(DiffResultLinker.CountUnlinkedContentItems(result) == 0, "all linked");

            int pairIdx = leftNote.StreamPairIndex;
            DiffResultLinker.MergeOneSidedTextsOnSamePair(result);
            List<DiffItem> merged = result.Items
                .Where(i => i != null && i.Kind == DiffKind.Text && i.StreamPairIndex == pairIdx)
                .ToList();
            Expect(merged.Count == 1, "B merge to 1 Text");
            Expect(
                merged.Count == 1
                    && !string.IsNullOrEmpty(merged[0].AddressLeft)
                    && !string.IsNullOrEmpty(merged[0].AddressRight),
                "B both addresses");
            Expect(
                merged.Count == 1
                    && merged[0].Summary != null
                    && merged[0].Summary.IndexOf("テキスト変更", StringComparison.Ordinal) >= 0,
                "B summary テキスト変更");
        }

        // D: Text のみマージ。同一 pair の Image / Structure は残す
        {
            var result = new DiffResult();
            result.Items.Add(new DiffItem
            {
                Kind = DiffKind.Text,
                AddressLeft = "A1",
                Summary = "left-only",
                StreamPairIndex = 3
            });
            result.Items.Add(new DiffItem
            {
                Kind = DiffKind.Text,
                AddressRight = "A2",
                Summary = "right-only",
                StreamPairIndex = 3
            });
            result.Items.Add(new DiffItem
            {
                Kind = DiffKind.Image,
                LeftImagePath = "x.png",
                StreamPairIndex = 3
            });
            result.Items.Add(new DiffItem
            {
                Kind = DiffKind.Structure,
                SheetLeft = "S",
                Summary = "左のみのシート: S"
            });
            DiffResultLinker.MergeOneSidedTextsOnSamePair(result);
            Expect(result.Items.Count(i => i.Kind == DiffKind.Text) == 1, "D text merged");
            Expect(result.Items.Count(i => i.Kind == DiffKind.Image) == 1, "D image kept");
            Expect(result.Items.Count(i => i.Kind == DiffKind.Structure) == 1, "D structure kept");
            DiffItem text = result.Items.First(i => i.Kind == DiffKind.Text);
            Expect(
                !string.IsNullOrEmpty(text.AddressLeft) && !string.IsNullOrEmpty(text.AddressRight),
                "D both addresses");
        }

        // E: 2 シートとも pair 0。生産経路 AttachExpandedLayouts → Merge で横断マージしない
        {
            var s1L = new SheetContent
            {
                Name = "S1",
                LooseCells = new List<CellContent> { Cell(1, 1, "same-s1") }
            };
            var s1R = new SheetContent
            {
                Name = "S1",
                LooseCells = new List<CellContent> { Cell(1, 1, "same-s1") }
            };
            var s2L = new SheetContent
            {
                Name = "S2",
                LooseCells = new List<CellContent> { Cell(1, 1, "same-s2") }
            };
            var s2R = new SheetContent
            {
                Name = "S2",
                LooseCells = new List<CellContent> { Cell(1, 1, "same-s2") }
            };
            var result = new DiffResult
            {
                LeftContent = new WorkbookContent
                {
                    Sheets = new List<SheetContent> { s1L, s2L }
                },
                RightContent = new WorkbookContent
                {
                    Sheets = new List<SheetContent> { s1R, s2R }
                },
                SheetPairs = new List<SheetPair>
                {
                    new SheetPair { LeftSheet = "S1", RightSheet = "S1" },
                    new SheetPair { LeftSheet = "S2", RightSheet = "S2" }
                }
            };
            var s1Left = new DiffItem
            {
                Kind = DiffKind.Text,
                SheetLeft = "S1",
                SheetRight = "S1",
                AddressLeft = "A1",
                Summary = "s1-left"
            };
            var s2Right = new DiffItem
            {
                Kind = DiffKind.Text,
                SheetLeft = "S2",
                SheetRight = "S2",
                AddressRight = "A1",
                Summary = "s2-right"
            };
            result.Items.Add(s1Left);
            result.Items.Add(s2Right);

            DiffResultLinker.AttachExpandedLayouts(result);
            Expect(s1Left.StreamPairIndex == 0, "E S1 pair 0");
            Expect(s2Right.StreamPairIndex == 0, "E S2 pair 0");

            DiffResultLinker.MergeOneSidedTextsOnSamePair(result);
            Expect(result.Items.Count == 2, "E no cross-sheet merge");
            Expect(
                result.Items.Contains(s1Left) && result.Items.Contains(s2Right),
                "E both items kept");
            Expect(
                string.IsNullOrEmpty(s1Left.AddressRight)
                    && string.IsNullOrEmpty(s2Right.AddressLeft),
                "E still one-sided");
        }

        // F: AddressLeft は左 LooseRow だけ、AddressRight は右 LooseRow だけ
        {
            var pairs = new List<ContentStreamPair>
            {
                new ContentStreamPair
                {
                    Op = AlignOp.SkipRight,
                    Right = new ContentStreamBlock { Kind = ContentBlockKind.LooseRow, Row = 5 }
                },
                new ContentStreamPair
                {
                    Op = AlignOp.SkipLeft,
                    Left = new ContentStreamBlock { Kind = ContentBlockKind.LooseRow, Row = 5 }
                },
                new ContentStreamPair
                {
                    Op = AlignOp.Match,
                    Left = new ContentStreamBlock { Kind = ContentBlockKind.LooseRow, Row = 10 },
                    Right = new ContentStreamBlock { Kind = ContentBlockKind.LooseRow, Row = 10 }
                }
            };
            var leftOnly = new DiffItem { Kind = DiffKind.Text, AddressLeft = "A5" };
            var rightOnly = new DiffItem { Kind = DiffKind.Text, AddressRight = "A5" };
            var result = new DiffResult();
            result.Items.Add(leftOnly);
            result.Items.Add(rightOnly);
            DiffResultLinker.Attach(result, pairs);
            Expect(leftOnly.StreamPairIndex == 1, "F AddressLeft → left LooseRow pair");
            Expect(rightOnly.StreamPairIndex == 0, "F AddressRight → right LooseRow pair");
        }

        Console.WriteLine(_fails == 0 ? "ALL PASS" : "FAILED " + _fails);
        Environment.Exit(_fails == 0 ? 0 : 1);
    }

    private static CellContent Cell(int row, int col, string text)
    {
        return new CellContent
        {
            Row = row,
            Column = col,
            Address = ((char)('A' + col - 1)).ToString() + row,
            Text = text
        };
    }
}
