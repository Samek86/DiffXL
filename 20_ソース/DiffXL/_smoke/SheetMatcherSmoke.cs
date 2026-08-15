using System;
using System.Collections.Generic;
using DiffXL.LOGIC.Diff;

internal static class SheetMatcherSmoke
{
    private static int _fails;
    private static void Expect(bool c, string n)
    {
        Console.WriteLine((c ? "OK " : "FAIL ") + n);
        if (!c) { _fails++; }
    }

    private static int Main()
    {
        var manual = new List<SheetPair>
        {
            new SheetPair { LeftSheet = "Cover", RightSheet = "表紙", IsManual = true }
        };
        SheetMatchResult r = SheetMatcher.Match(
            new[] { "Cover", "Data" },
            new[] { "表紙", "Data" },
            manual);
        Expect(r.Pairs.Count == 2, "pairs=2");
        Expect(r.Pairs.Exists(p => p.IsManual
            && p.LeftSheet == "Cover" && p.RightSheet == "表紙"), "manual cover");
        Expect(r.Pairs.Exists(p => !p.IsManual
            && string.Equals(p.LeftSheet, "Data", StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.RightSheet, "Data", StringComparison.OrdinalIgnoreCase)), "auto data");
        Expect(r.LeftOnlySheets.Count == 0 && r.RightOnlySheets.Count == 0, "no leftovers");

        SheetMatchResult none = SheetMatcher.Match(
            new[] { "A" }, new[] { "A" }, null);
        Expect(none.Pairs.Count == 1 && !none.Pairs[0].IsManual, "null manual still autos");

        Console.WriteLine(_fails == 0 ? "ALL PASS" : "FAILED " + _fails);
        return _fails == 0 ? 0 : 1;
    }
}
