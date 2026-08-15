// Diff pair indices: Skip ∪ non-Structure StreamPairIndex; wrap next/prev
// Compile against built DiffXL (csc /r:DiffXL.exe). WPF 非依存の静的 API のみ使う。

using System;
using System.Collections.Generic;
using System.Linq;
using DiffXL.LOGIC.Diff;
using DiffXL.VIEW.Controls;

internal static class DiffPairIndexSmoke
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

    private static void ExpectSeq(IList<int> actual, int[] expected, string name)
    {
        bool ok = actual != null
            && actual.Count == expected.Length
            && actual.SequenceEqual(expected);
        if (ok)
        {
            Console.WriteLine("OK " + name);
        }
        else
        {
            string got = actual == null ? "null" : string.Join(",", actual);
            Console.WriteLine("FAIL " + name + " got=[" + got + "] expected=[" + string.Join(",", expected) + "]");
            _fails++;
        }
    }

    private static ContentStreamPair Pair(AlignOp op)
    {
        return new ContentStreamPair { Op = op };
    }

    private static ContentStreamPair TypedPair(AlignOp op, ContentBlockKind kind)
    {
        var left = new ContentStreamBlock { Kind = kind };
        var pair = new ContentStreamPair { Op = op, Left = left };
        if (op != AlignOp.SkipLeft)
        {
            pair.Right = new ContentStreamBlock { Kind = kind };
        }

        if (op == AlignOp.SkipRight)
        {
            pair.Left = null;
        }

        return pair;
    }

    private static DiffItem Item(DiffKind kind, int streamPairIndex)
    {
        return new DiffItem { Kind = kind, StreamPairIndex = streamPairIndex };
    }

    private static int Main()
    {
        Console.WriteLine("DiffPairIndexSmoke");

        // 空・null
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(null, null),
            new int[0],
            "empty null/null");
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(new List<ContentStreamPair>(), new List<DiffItem>()),
            new int[0],
            "empty lists");

        // Skip は含め、Match は含めない
        IList<ContentStreamPair> pairs = new List<ContentStreamPair>
        {
            Pair(AlignOp.Match),
            Pair(AlignOp.SkipLeft),
            Pair(AlignOp.Match),
            Pair(AlignOp.SkipRight),
            null
        };
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(pairs, null),
            new[] { 1, 3 },
            "skip pairs only");

        // 非 Structure の StreamPairIndex は Match pair でも含める
        IList<DiffItem> items = new List<DiffItem>
        {
            Item(DiffKind.Text, 0),
            Item(DiffKind.Structure, 2),
            Item(DiffKind.TableCellChange, 2),
            Item(DiffKind.Image, -1),
            null
        };
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(pairs, items),
            new[] { 0, 1, 2, 3 },
            "union skip + content items (Structure/neg excluded)");

        // 範囲外 StreamPairIndex は捨てる
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(pairs, new[] { Item(DiffKind.Text, 99) }),
            new[] { 1, 3 },
            "out-of-range item ignored");

        // 昇順・重複なし
        IList<int> union = DiffPairNavigator.CollectDiffPairIndices(
            new List<ContentStreamPair> { Pair(AlignOp.SkipLeft), Pair(AlignOp.SkipLeft) },
            new[] { Item(DiffKind.Text, 0), Item(DiffKind.Image, 0) });
        ExpectSeq(union, new[] { 0, 1 }, "sorted unique");

        // 循環
        int[] diffs = { 1, 4, 7 };
        Expect(DiffPairNavigator.PickNextDiffPairIndex(null, 0, 1) == -1, "pick empty null");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(new int[0], 0, 1) == -1, "pick empty list");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(diffs, -1, 1) == 1, "next from before first");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(diffs, 1, 1) == 4, "next from on first");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(diffs, 4, 1) == 7, "next from middle");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(diffs, 7, 1) == 1, "next wrap last→first");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(diffs, 5, 1) == 7, "next from between");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(diffs, 1, -1) == 7, "prev wrap first→last");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(diffs, 4, -1) == 1, "prev from middle");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(diffs, 7, -1) == 4, "prev from last");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(diffs, 0, -1) == 7, "prev from before first");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(new[] { 3 }, 3, 1) == 3, "single next wraps self");
        Expect(DiffPairNavigator.PickNextDiffPairIndex(new[] { 3 }, 3, -1) == 3, "single prev wraps self");

        // 種類フィルタ: 本文は残し、ジャンプ対象だけ絞る
        IList<ContentStreamPair> mixed = new List<ContentStreamPair>
        {
            TypedPair(AlignOp.Match, ContentBlockKind.LooseRow),
            TypedPair(AlignOp.SkipLeft, ContentBlockKind.TableRow),
            TypedPair(AlignOp.Match, ContentBlockKind.Image),
            TypedPair(AlignOp.SkipRight, ContentBlockKind.LooseRow),
            TypedPair(AlignOp.Match, ContentBlockKind.TableHeader),
            TypedPair(AlignOp.SkipLeft, ContentBlockKind.Shape)
        };
        IList<DiffItem> mixedItems = new List<DiffItem>
        {
            Item(DiffKind.Text, 0),
            Item(DiffKind.Image, 2),
            Item(DiffKind.TableCellChange, 4)
        };
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(mixed, mixedItems),
            new[] { 0, 1, 2, 3, 4, 5 },
            "filter All default (2-arg)");
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(mixed, mixedItems, StreamKindFilter.All),
            new[] { 0, 1, 2, 3, 4, 5 },
            "filter All");
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(mixed, mixedItems, StreamKindFilter.Table),
            new[] { 1, 4 },
            "filter Table = TableRow skip + TableHeader/TableCellChange");
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(mixed, mixedItems, StreamKindFilter.Image),
            new[] { 2 },
            "filter Image");
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(mixed, mixedItems, StreamKindFilter.Cell),
            new[] { 0, 3 },
            "filter Cell = LooseRow only");

        // Table* DiffKind はブロック種別が無くても表に含める
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(
                new List<ContentStreamPair> { Pair(AlignOp.Match) },
                new[] { Item(DiffKind.TableRowDelete, 0) },
                StreamKindFilter.Table),
            new[] { 0 },
            "filter Table via Table* DiffKind without block");

        // Image* DiffKind
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(
                new List<ContentStreamPair> { Pair(AlignOp.Match) },
                new[] { Item(DiffKind.ImageOnlyRight, 0) },
                StreamKindFilter.Image),
            new[] { 0 },
            "filter Image via ImageOnlyRight");

        // Text on TableRow は Cell に入れない
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(
                new List<ContentStreamPair> { TypedPair(AlignOp.Match, ContentBlockKind.TableRow) },
                new[] { Item(DiffKind.Text, 0) },
                StreamKindFilter.Cell),
            new int[0],
            "filter Cell excludes TableRow text");
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(
                new List<ContentStreamPair> { TypedPair(AlignOp.Match, ContentBlockKind.TableRow) },
                new[] { Item(DiffKind.Text, 0) },
                StreamKindFilter.Table),
            new[] { 0 },
            "filter Table includes TableRow text");

        // 該当なし
        ExpectSeq(
            DiffPairNavigator.CollectDiffPairIndices(
                new List<ContentStreamPair> { TypedPair(AlignOp.SkipLeft, ContentBlockKind.Shape) },
                null,
                StreamKindFilter.Table),
            new int[0],
            "filter Table empty when only Shape skip");

        if (_fails > 0)
        {
            Console.WriteLine("FAILED " + _fails);
            return 1;
        }

        Console.WriteLine("PASS DiffPairIndexSmoke");
        return 0;
    }
}
