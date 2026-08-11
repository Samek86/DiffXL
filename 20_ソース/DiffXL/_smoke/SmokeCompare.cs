using System;
using System.IO;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

class SmokeCompare
{
    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: left right"); return 2; }
        AppPaths.EnsureDirectories();
        NativeBootstrap.EnsureNativeBinaries();
        var engine = new DiffEngine();
        var result = engine.Compare(args[0], args[1]);
        Console.WriteLine("Error=" + (result.ErrorMessage ?? ""));
        Console.WriteLine("Count=" + result.Items.Count);
        Console.WriteLine("ElapsedMs=" + (int)result.Elapsed.TotalMilliseconds);
        foreach (var i in result.Items)
            Console.WriteLine("[" + i.Kind + "] " + i.Summary);
        return string.IsNullOrEmpty(result.ErrorMessage) ? 0 : 1;
    }
}
