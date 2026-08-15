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
