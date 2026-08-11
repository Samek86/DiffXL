using System;
using System.IO;
using System.Linq;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;
using DiffXL.LOGIC.Excel;

/// <summary>
/// 出荷コード ExcelWorkbookSession / DiffEngine を実ファイルで叩くスモーク。
/// </summary>
class SmokeGoto
{
    static int Main(string[] args)
    {
        string left = args.Length > 0
            ? args[0]
            : @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_left.xlsx";
        string right = args.Length > 1
            ? args[1]
            : @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_right.xlsx";

        AppPaths.EnsureDirectories();
        NativeBootstrap.EnsureNativeBinaries();
        Log.Info("=== SmokeGoto start ===");
        Log.Info("left=" + left);
        Log.Info("right=" + right);

        if (!File.Exists(left) || !File.Exists(right))
        {
            Console.WriteLine("MISSING_FILES");
            Log.Error("sample missing");
            return 2;
        }

        // --- DiffEngine (shipped path) ---
        var engine = new DiffEngine();
        DiffResult result = engine.Compare(left, right);
        Console.WriteLine("COMPARE_ERROR=" + (result.ErrorMessage ?? ""));
        Console.WriteLine("COMPARE_COUNT=" + result.Items.Count);
        int text = 0, image = 0, only = 0, structure = 0;
        foreach (var i in result.Items)
        {
            if (i.Kind == DiffKind.Text) text++;
            else if (i.Kind == DiffKind.Image) image++;
            else if (i.Kind == DiffKind.ImageOnlyLeft || i.Kind == DiffKind.ImageOnlyRight) only++;
            else if (i.Kind == DiffKind.Structure) structure++;
            Console.WriteLine("ITEM [" + i.Kind + "] " + i.Summary
                + " addrL=" + i.AddressLeft + " order=" + i.OrderHint);
        }
        Console.WriteLine("COUNTS text=" + text + " image=" + image + " only=" + only + " structure=" + structure);
        Log.Info("COMPARE_COUNT=" + result.Items.Count);

        // --- Open + Goto (shipped path) ---
        int fail = 0;
        using (var session = new ExcelWorkbookSession())
        {
            try
            {
                session.OpenReadOnly(left);
                Console.WriteLine("OPEN_OK hwnd=" + session.GetMainWindowHandle());
                Log.Info("OPEN_OK");

                // activate long sheet if present
                try
                {
                    var sheets = session.GetSheetNames();
                    Console.WriteLine("SHEETS=" + string.Join(",", sheets));
                    if (sheets.Contains("長い一覧"))
                    {
                        session.ActivateSheet("長い一覧");
                        Console.WriteLine("SHEET_ACTIVE=長い一覧");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("SHEET_ERR=" + ex.Message);
                    Log.Exception(ex);
                }

                int[] rows = new[] { 1, 11, 25, 50 };
                foreach (int row in rows)
                {
                    bool okGoto = session.TryGotoRow(row);
                    bool okScroll = session.TrySetScroll(row, 1);
                    int sr, sc;
                    bool okGet = session.TryGetScroll(out sr, out sc);
                    Console.WriteLine("ROW " + row + " goto=" + okGoto + " setScroll=" + okScroll
                        + " getScroll=" + okGet + " sr=" + sr + " sc=" + sc);
                    Log.Info("ROW " + row + " goto=" + okGoto + " setScroll=" + okScroll
                        + " get=" + okGet + " sr=" + sr);
                    if (!okGoto && !okScroll)
                    {
                        fail++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SESSION_FAIL=" + ex);
                Log.Exception(ex);
                fail += 10;
            }
        }

        Console.WriteLine("FAIL_SCORE=" + fail);
        Log.Info("=== SmokeGoto end fail=" + fail + " ===");
        return fail == 0 ? 0 : 1;
    }
}
