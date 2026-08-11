using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using DiffXL.COMMON;
using DiffXL.LOGIC.Excel;
using DiffXL.VIEW.Controls;

/// <summary>
/// Excel を HwndHost に埋め込んだ状態で TryGotoRow が動くか検証する。
/// </summary>
class SmokeEmbedGoto
{
    [STAThread]
    static int Main(string[] args)
    {
        string path = args.Length > 0
            ? args[0]
            : @"C:\JUN\WORK\DiffXL\30_参考資料\samples\full_feature_left.xlsx";

        AppPaths.EnsureDirectories();
        Log.Info("=== SmokeEmbedGoto start ===");

        var app = new Application();
        var win = new Window
        {
            Title = "SmokeEmbedGoto",
            Width = 900,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var host = new ExcelHostControl();
        win.Content = host;

        int fail = 0;
        ExcelWorkbookSession session = null;

        win.Loaded += (s, e) =>
        {
            try
            {
                session = new ExcelWorkbookSession();
                session.OpenReadOnly(path);
                IntPtr hwnd = session.GetMainWindowHandle();
                Console.WriteLine("OPEN hwnd=" + hwnd);
                host.Attach(hwnd);
                host.ResizeExcelToHost();

                try { session.ActivateSheet("長い一覧"); } catch { /* ignore */ }

                // レイアウト後にジャンプ
                win.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    host.ResizeExcelToHost();
                    int[] rows = { 1, 11, 25, 40 };
                    foreach (int row in rows)
                    {
                        bool ok = session.TryGotoRow(row);
                        int sr = -1, sc = -1;
                        bool got = session.TryGetScroll(out sr, out sc);
                        int delta = got ? Math.Abs(sr - row) : 999;
                        Console.WriteLine("EMBED_ROW " + row + " goto=" + ok + " get=" + got + " sr=" + sr + " delta=" + delta);
                        Log.Info("EMBED_ROW " + row + " goto=" + ok + " get=" + got + " sr=" + sr + " delta=" + delta);

                        // goto 失敗、取得失敗、または |sr-row|>2 はすべて失敗
                        if (!ok)
                        {
                            fail++;
                            Console.WriteLine("EMBED_FAIL_GOTO row=" + row);
                        }
                        else if (!got)
                        {
                            fail++;
                            Console.WriteLine("EMBED_FAIL_GET row=" + row);
                        }
                        else if (delta > 2)
                        {
                            fail++;
                            Console.WriteLine("EMBED_FAIL_ACCURACY row=" + row + " sr=" + sr + " |delta|=" + delta);
                            Log.Error("EMBED_FAIL_ACCURACY row=" + row + " sr=" + sr);
                        }
                    }

                    Console.WriteLine("EMBED_FAIL=" + fail);
                    Log.Info("EMBED_FAIL=" + fail);
                    win.Close();
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine("EMBED_ERR=" + ex);
                Log.Exception(ex);
                fail = 99;
                win.Close();
            }
        };

        win.Closed += (s, e) =>
        {
            try { host.Detach(); } catch { /* ignore */ }
            try { if (session != null) session.Dispose(); } catch { /* ignore */ }
            app.Shutdown();
        };

        app.Run(win);
        Console.WriteLine("FINAL_FAIL=" + fail);
        return fail == 0 ? 0 : 1;
    }
}
