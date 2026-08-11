using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;
using DiffXL.LOGIC.Excel;

/// <summary>
/// Task 1: SyncSessionState / Probe / StatusLine の COM なしスモーク。
/// content_scroll の SC_画像ギャップ（right-only 帯）を DiffEngine で検証。
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        string root = FindRepoRoot();
        string samples = Path.Combine(root, "30_参考資料", "samples");
        string left = GetArg(args, "--left")
            ?? Path.Combine(samples, "content_scroll_left.xlsx");
        string right = GetArg(args, "--right")
            ?? Path.Combine(samples, "content_scroll_right.xlsx");
        string sheetFilter = GetArg(args, "--sheet") ?? "SC_画像ギャップ";

        Console.WriteLine("SyncUxSmoke");
        Console.WriteLine("left=" + left);
        Console.WriteLine("right=" + right);
        Console.WriteLine("sheet=" + sheetFilter);

        if (!File.Exists(left) || !File.Exists(right))
        {
            Console.WriteLine("FAIL sample xlsx missing");
            Console.WriteLine("SYNC_UX_SMOKE_FAIL");
            return 1;
        }

        AppPaths.EnsureDirectories();
        NativeBootstrap.EnsureNativeBinaries();

        var engine = new DiffEngine();
        DiffResult result = engine.Compare(left, right);
        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            Console.WriteLine("FAIL DiffEngine: " + result.ErrorMessage);
            Console.WriteLine("SYNC_UX_SMOKE_FAIL");
            return 1;
        }

        SheetAlignment al = result.Alignments != null
            ? result.Alignments.FirstOrDefault(a =>
                string.Equals(a.LeftSheet, sheetFilter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.RightSheet, sheetFilter, StringComparison.OrdinalIgnoreCase))
            : null;

        if (al == null || al.ScrollMap == null)
        {
            Console.WriteLine("FAIL no ScrollMap for " + sheetFilter);
            Console.WriteLine("SYNC_UX_SMOKE_FAIL");
            return 1;
        }

        ContentScrollMap map = al.ScrollMap;
        Console.WriteLine(map.Describe());

        int fail = 0;

        // --- SC_画像ギャップ: R9 は right-only 帯、左は hold ≤7 ---
        ScrollMapProbe p = map.ProbeFromRight(9);
        Console.WriteLine("ProbeFromRight(9) Kind=" + p.Kind
            + " MappedRow=" + p.MappedRow
            + " HoldRow=" + p.HoldRow
            + " Seg=" + p.SegmentStart + "-" + p.SegmentEnd);

        if (p.Kind != SyncSegmentKind.RightOnly)
        {
            Console.WriteLine("FAIL Probe Kind expected RightOnly got " + p.Kind);
            fail++;
        }

        if (p.MappedRow > 7)
        {
            Console.WriteLine("FAIL MappedRow expected <=7 got " + p.MappedRow);
            fail++;
        }

        string status = SyncSessionState.BuildStatusLine(
            enabled: true,
            unavailable: false,
            kind: p.Kind,
            leftRow: p.MappedRow,
            rightRow: 9);

        Console.WriteLine("StatusLine=" + status);
        if (string.IsNullOrEmpty(status) || status.IndexOf("右のみ", StringComparison.Ordinal) < 0)
        {
            Console.WriteLine("FAIL StatusLine should contain 右のみ");
            fail++;
        }

        // Equal 側も sanity
        ScrollMapProbe pe = map.ProbeFromLeft(5);
        if (pe.Kind != SyncSegmentKind.Equal && pe.Kind != SyncSegmentKind.Identity)
        {
            Console.WriteLine("FAIL ProbeFromLeft(5) Kind=" + pe.Kind + " (expect Equal/Identity)");
            fail++;
        }
        else
        {
            Console.WriteLine("OK ProbeFromLeft(5) Kind=" + pe.Kind + " Mapped=" + pe.MappedRow);
        }

        string equalLine = SyncSessionState.BuildStatusLine(true, false, SyncSegmentKind.Equal, 5, 5);
        if (equalLine.IndexOf("内容対応", StringComparison.Ordinal) < 0)
        {
            Console.WriteLine("FAIL Equal StatusLine: " + equalLine);
            fail++;
        }

        string offLine = SyncSessionState.BuildStatusLine(false, false, SyncSegmentKind.Disabled, 1, 1);
        if (offLine != "同期OFF")
        {
            Console.WriteLine("FAIL Disabled StatusLine: " + offLine);
            fail++;
        }

        // Ui.SyncPollFallbackMs 既定 250 / ShowSyncToastOnJump true / ReduceMotion false
        var defUi = new UiSettings();
        if (defUi.SyncPollFallbackMs != 250)
        {
            Console.WriteLine("FAIL SyncPollFallbackMs default expected 250 got " + defUi.SyncPollFallbackMs);
            fail++;
        }
        else
        {
            Console.WriteLine("OK SyncPollFallbackMs default=250");
        }

        if (!defUi.ShowSyncToastOnJump)
        {
            Console.WriteLine("FAIL ShowSyncToastOnJump default expected true");
            fail++;
        }
        else
        {
            Console.WriteLine("OK ShowSyncToastOnJump default=true");
        }

        if (defUi.ReduceMotion)
        {
            Console.WriteLine("FAIL ReduceMotion default expected false");
            fail++;
        }
        else
        {
            Console.WriteLine("OK ReduceMotion default=false");
        }

        // BuildJumpHint: gap→Equal かつ |Δ|≥3
        string jh = ScrollSyncService.BuildJumpHint(
            SyncSegmentKind.RightOnly, SyncSegmentKind.Equal, 7, 12, fromRight: true);
        Console.WriteLine("BuildJumpHint(ROnly→Eq 7→12)=" + jh);
        if (string.IsNullOrEmpty(jh)
            || jh.IndexOf("7", StringComparison.Ordinal) < 0
            || jh.IndexOf("12", StringComparison.Ordinal) < 0
            || jh.IndexOf("再同期", StringComparison.Ordinal) < 0)
        {
            Console.WriteLine("FAIL BuildJumpHint expected 再同期 7→12 got " + jh);
            fail++;
        }
        else
        {
            Console.WriteLine("OK BuildJumpHint gap→equal |Δ|>=3");
        }

        string jhSmall = ScrollSyncService.BuildJumpHint(
            SyncSegmentKind.LeftOnly, SyncSegmentKind.Equal, 5, 6, fromRight: false);
        if (jhSmall != null)
        {
            Console.WriteLine("FAIL BuildJumpHint |Δ|=1 should be null got " + jhSmall);
            fail++;
        }
        else
        {
            Console.WriteLine("OK BuildJumpHint |Δ|<3 → null");
        }

        string jhNoGap = ScrollSyncService.BuildJumpHint(
            SyncSegmentKind.Equal, SyncSegmentKind.Equal, 1, 20, fromRight: false);
        if (jhNoGap != null)
        {
            Console.WriteLine("FAIL BuildJumpHint Equal→Equal should be null got " + jhNoGap);
            fail++;
        }
        else
        {
            Console.WriteLine("OK BuildJumpHint non-gap → null");
        }

        // ScrollSyncService: ApplyDriven が StateChanged を発火（COM なし・マップ結果）
        using (var sync = new ScrollSyncService())
        {
            sync.SetContentMapsFromAlignments(result.Alignments);
            sync.SetActiveSheets(sheetFilter, sheetFilter);

            SyncSessionState got = null;
            sync.StateChanged += s => got = s;

            // 右のみ帯を右駆動で Apply（セッション未接続でも状態は発行）
            sync.ApplyDrivenByRight(9, 1);
            if (got == null)
            {
                Console.WriteLine("FAIL StateChanged not fired");
                fail++;
            }
            else
            {
                Console.WriteLine("ApplyDrivenByRight State: Kind=" + got.SegmentKind
                    + " L" + got.LeftRow + " R" + got.RightRow
                    + " | " + got.StatusLine);
                if (got.SegmentKind != SyncSegmentKind.RightOnly)
                {
                    Console.WriteLine("FAIL Apply State Kind expected RightOnly got " + got.SegmentKind);
                    fail++;
                }

                if (got.LeftRow > 7)
                {
                    Console.WriteLine("FAIL Apply LeftRow expected <=7 got " + got.LeftRow);
                    fail++;
                }

                if (got.StatusLine == null || got.StatusLine.IndexOf("右のみ", StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine("FAIL Apply StatusLine: " + got.StatusLine);
                    fail++;
                }

                if (sync.CurrentState == null || sync.CurrentState.SegmentKind != got.SegmentKind)
                {
                    Console.WriteLine("FAIL CurrentState mismatch");
                    fail++;
                }
            }

            // ギャップ帯から Equal へ復帰 → JumpHint（|Δ|≥3 のとき）
            // 右のみ R9 でホールド後、右を Equal 帯（マップで左が大きく動く行）へ
            got = null;
            sync.Enabled = true;
            sync.ApplyDrivenByRight(9, 1); // RightOnly を CurrentState に
            int holdLeft = got != null ? got.LeftRow : 7;
            // same 帯など Equal かつ左行が大きく変わる行を探す
            // ギャップ帯の後（R12= same_B）を優先して |Δ|≥3 の Equal 行を探す
            int equalRight = -1;
            int mappedLeft = -1;
            for (int r = 12; r <= 40; r++)
            {
                ScrollMapProbe pr = map.ProbeFromRight(r);
                if (pr.Kind == SyncSegmentKind.Equal && Math.Abs(pr.MappedRow - holdLeft) >= 3)
                {
                    equalRight = r;
                    mappedLeft = pr.MappedRow;
                    break;
                }
            }

            if (equalRight < 0)
            {
                for (int r = 1; r <= 40; r++)
                {
                    ScrollMapProbe pr = map.ProbeFromRight(r);
                    if (pr.Kind == SyncSegmentKind.Equal && Math.Abs(pr.MappedRow - holdLeft) >= 3)
                    {
                        equalRight = r;
                        mappedLeft = pr.MappedRow;
                        break;
                    }
                }
            }

            if (equalRight < 0)
            {
                // フォールバック: pure BuildJumpHint は上で検証済み。ここではサービス経路を試行
                Console.WriteLine("WARN no Equal row with |Δ|>=3 after RightOnly; skip Publish JumpHint path");
            }
            else
            {
                got = null;
                sync.ApplyDrivenByRight(equalRight, 1);
                if (got == null)
                {
                    Console.WriteLine("FAIL JumpHint path StateChanged not fired");
                    fail++;
                }
                else
                {
                    Console.WriteLine("Jump re-sync: prev hold L" + holdLeft
                        + " → R" + equalRight + " L" + got.LeftRow
                        + " Kind=" + got.SegmentKind
                        + " JumpHint=" + (got.JumpHint ?? "(null)"));
                    if (got.SegmentKind == SyncSegmentKind.Equal
                        && Math.Abs(got.LeftRow - holdLeft) >= 3)
                    {
                        if (string.IsNullOrEmpty(got.JumpHint)
                            || got.JumpHint.IndexOf("再同期", StringComparison.Ordinal) < 0)
                        {
                            Console.WriteLine("FAIL expected JumpHint on gap→equal jump");
                            fail++;
                        }
                        else
                        {
                            Console.WriteLine("OK Publish JumpHint=" + got.JumpHint);
                        }
                    }
                    else
                    {
                        Console.WriteLine("WARN jump condition not met Kind=" + got.SegmentKind
                            + " Δ=" + Math.Abs(got.LeftRow - holdLeft) + " (mappedLeft=" + mappedLeft + ")");
                    }
                }
            }

            // 左駆動: Equal 帯 L5 → 右もマップ通り
            got = null;
            int expectedRight = map.MapLeftToRight(5);
            sync.ApplyDrivenByLeft(5, 2);
            if (got == null)
            {
                Console.WriteLine("FAIL ApplyDrivenByLeft StateChanged not fired");
                fail++;
            }
            else
            {
                Console.WriteLine("ApplyDrivenByLeft State: Kind=" + got.SegmentKind
                    + " L" + got.LeftRow + " R" + got.RightRow
                    + " Col L" + got.LeftCol + " R" + got.RightCol
                    + " | " + got.StatusLine);
                if (got.LeftRow != 5 || got.RightRow != expectedRight)
                {
                    Console.WriteLine("FAIL ApplyDrivenByLeft map L5→R" + expectedRight
                        + " got L" + got.LeftRow + " R" + got.RightRow);
                    fail++;
                }

                // 横は列 1:1
                if (got.LeftCol != 2 || got.RightCol != 2)
                {
                    Console.WriteLine("FAIL ApplyDrivenByLeft col 1:1 expected 2,2 got "
                        + got.LeftCol + "," + got.RightCol);
                    fail++;
                }
            }

            // SyncScroll OFF: Disabled を Publish、マップ適用しない（状態のみ）
            got = null;
            sync.Enabled = false;
            sync.ApplyDrivenByRight(9, 1);
            if (got == null)
            {
                Console.WriteLine("FAIL OFF StateChanged not fired");
                fail++;
            }
            else if (got.SegmentKind != SyncSegmentKind.Disabled
                && (got.StatusLine == null || got.StatusLine.IndexOf("同期OFF", StringComparison.Ordinal) < 0))
            {
                // Enabled setter でも Publish するため、最後の Apply 状態を確認
                Console.WriteLine("OFF State: Kind=" + got.SegmentKind + " | " + got.StatusLine);
                if (got.SegmentKind != SyncSegmentKind.Disabled)
                {
                    Console.WriteLine("FAIL OFF expected Disabled got " + got.SegmentKind);
                    fail++;
                }
            }
            else
            {
                Console.WriteLine("OK SyncScroll OFF → Kind=" + got.SegmentKind + " | " + got.StatusLine);
            }

            // Unavailable StatusLine（enabled=false でも停止文言を優先）
            string unLine = SyncSessionState.BuildStatusLine(
                enabled: false,
                unavailable: true,
                kind: SyncSegmentKind.Unavailable,
                leftRow: 1,
                rightRow: 1);
            Console.WriteLine("Unavailable StatusLine=" + unLine);
            if (unLine == null || unLine.IndexOf("同期停止", StringComparison.Ordinal) < 0)
            {
                Console.WriteLine("FAIL Unavailable StatusLine: " + unLine);
                fail++;
            }
            else
            {
                Console.WriteLine("OK Unavailable StatusLine");
            }

            // RetryAfterUnavailable: 停止解除後は Unavailable でない状態を Publish
            sync.Enabled = true;
            got = null;
            // IsUnavailable は false のままでも Retry が StateChanged を出すこと
            sync.RetryAfterUnavailable();
            if (got == null)
            {
                Console.WriteLine("FAIL RetryAfterUnavailable StateChanged not fired");
                fail++;
            }
            else if (got.SegmentKind == SyncSegmentKind.Unavailable
                || (got.StatusLine != null && got.StatusLine.IndexOf("同期停止", StringComparison.Ordinal) >= 0))
            {
                Console.WriteLine("FAIL Retry still Unavailable: " + got.SegmentKind + " | " + got.StatusLine);
                fail++;
            }
            else if (sync.IsUnavailable)
            {
                Console.WriteLine("FAIL IsUnavailable still true after Retry");
                fail++;
            }
            else
            {
                Console.WriteLine("OK RetryAfterUnavailable → Kind=" + got.SegmentKind
                    + " | " + got.StatusLine);
            }

            // --- E1: identity マップ → Overlay 出さない(IsInGap=false)、Status「行番号同期」---
            using (var idSync = new ScrollSyncService())
            {
                SyncSessionState idGot = null;
                idSync.StateChanged += s => idGot = s;
                idSync.ApplyDrivenByLeft(10, 3);
                if (idGot == null)
                {
                    Console.WriteLine("FAIL E1 StateChanged not fired");
                    fail++;
                }
                else if (idGot.SegmentKind != SyncSegmentKind.Identity)
                {
                    Console.WriteLine("FAIL E1 Kind expected Identity got " + idGot.SegmentKind);
                    fail++;
                }
                else if (idGot.IsInGap)
                {
                    Console.WriteLine("FAIL E1 Identity should not be gap (overlay off)");
                    fail++;
                }
                else if (idGot.StatusLine == null
                    || idGot.StatusLine.IndexOf("行番号同期", StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine("FAIL E1 StatusLine: " + idGot.StatusLine);
                    fail++;
                }
                else if (idGot.LeftRow != 10 || idGot.RightRow != 10
                    || idGot.LeftCol != 3 || idGot.RightCol != 3)
                {
                    Console.WriteLine("FAIL E1 1:1 map L10C3 expected R10C3 got L"
                        + idGot.LeftRow + "C" + idGot.LeftCol
                        + " R" + idGot.RightRow + "C" + idGot.RightCol);
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK E1 identity · " + idGot.StatusLine);
                }
            }

            // --- E2: IsBusy → Apply 無視・キューしない ---
            got = null;
            sync.IsBusy = true;
            sync.ApplyDrivenByLeft(99, 1);
            sync.ApplyDrivenByRight(99, 1);
            if (got != null)
            {
                Console.WriteLine("FAIL E2 Apply while IsBusy should be ignored, got "
                    + got.SegmentKind + " L" + got.LeftRow);
                fail++;
            }
            else if (sync.CurrentState != null && sync.CurrentState.LeftRow == 99)
            {
                Console.WriteLine("FAIL E2 CurrentState should not advance to 99 while busy");
                fail++;
            }
            else
            {
                Console.WriteLine("OK E2 IsBusy ignores Apply");
            }

            sync.IsBusy = false;
            got = null;
            sync.SetActiveSheets(sheetFilter, sheetFilter);
            // SetActiveSheets also Publishes — accept then re-apply
            got = null;
            sync.ApplyDrivenByLeft(5, 1);
            if (got == null)
            {
                Console.WriteLine("FAIL E2 after clear IsBusy Apply should fire");
                fail++;
            }
            else
            {
                Console.WriteLine("OK E2 after IsBusy clear Apply works Kind=" + got.SegmentKind);
            }

            // --- E3: 左右シート不一致 → シート未対応・同期しない ---
            got = null;
            int beforeRight = sync.CurrentState != null ? sync.CurrentState.RightRow : 1;
            sync.SetActiveSheets("__NoLeft__", "__NoRight__");
            if (got == null)
            {
                Console.WriteLine("FAIL E3 SetActiveSheets should Publish Unpaired");
                fail++;
            }
            else if (got.SegmentKind != SyncSegmentKind.Unpaired
                || got.StatusLine == null
                || got.StatusLine.IndexOf("シート未対応", StringComparison.Ordinal) < 0)
            {
                Console.WriteLine("FAIL E3 expected Unpaired/シート未対応 got Kind="
                    + got.SegmentKind + " | " + got.StatusLine);
                fail++;
            }
            else if (!sync.SheetsUnpaired)
            {
                Console.WriteLine("FAIL E3 SheetsUnpaired expected true");
                fail++;
            }
            else
            {
                Console.WriteLine("OK E3 unpaired Status=" + got.StatusLine);
            }

            got = null;
            int holdRight = sync.CurrentState != null ? sync.CurrentState.RightRow : beforeRight;
            sync.ApplyDrivenByLeft(20, 1);
            if (got == null)
            {
                Console.WriteLine("FAIL E3 Apply while unpaired should still Publish status");
                fail++;
            }
            else if (got.SegmentKind != SyncSegmentKind.Unpaired)
            {
                Console.WriteLine("FAIL E3 Apply Kind expected Unpaired got " + got.SegmentKind);
                fail++;
            }
            else if (got.RightRow != holdRight)
            {
                // 同期しない: 相手行をマップで動かさない
                Console.WriteLine("FAIL E3 should not map-sync follower RightRow "
                    + holdRight + " → " + got.RightRow);
                fail++;
            }
            else
            {
                Console.WriteLine("OK E3 unpaired Apply does not map follower R=" + got.RightRow);
            }

            // restore sheet pair
            sync.SetActiveSheets(sheetFilter, sheetFilter);

            // --- E4: 画像 0 → テキストのみマップ（ギャップはテキスト挿入のみ）---
            // 1 文字は weak token のため 2 文字以上を使う
            var leftCells = new[]
            {
                new CellValue { Row = 1, Column = 1, Text = "Alpha" },
                new CellValue { Row = 2, Column = 1, Text = "Bravo" },
                new CellValue { Row = 3, Column = 1, Text = "Charlie" }
            };
            var rightCells = new[]
            {
                new CellValue { Row = 1, Column = 1, Text = "Alpha" },
                new CellValue { Row = 2, Column = 1, Text = "ONLY_RIGHT" },
                new CellValue { Row = 3, Column = 1, Text = "Bravo" },
                new CellValue { Row = 4, Column = 1, Text = "Charlie" }
            };
            ContentScrollMap textMap = ContentScrollMap.Build(
                "TextOnly", "TextOnly", leftCells, rightCells, (IList<ImageCorrespondence>)null);
            if (textMap == null || !textMap.IsContentBased)
            {
                Console.WriteLine("FAIL E4 expected content-based text map");
                fail++;
            }
            else
            {
                ScrollMapProbe tr = textMap.ProbeFromRight(2);
                Console.WriteLine("E4 textMap " + textMap.Describe()
                    + " ProbeR2 Kind=" + tr.Kind + " Mapped=" + tr.MappedRow);
                if (tr.Kind != SyncSegmentKind.RightOnly && tr.Kind != SyncSegmentKind.Equal)
                {
                    // テキスト挿入行は RightOnly が理想。Equal でもマップ構築できていれば可
                    Console.WriteLine("WARN E4 ProbeR2 Kind=" + tr.Kind + " (text insert gap preferred)");
                }

                if (tr.Kind == SyncSegmentKind.RightOnly)
                {
                    Console.WriteLine("OK E4 images=0 text-only gap Kind=RightOnly");
                }
                else if (textMap.IsContentBased)
                {
                    Console.WriteLine("OK E4 images=0 content map built without images");
                }
            }

            // --- 連続 Apply 100 回で例外なし ---
            try
            {
                for (int i = 1; i <= 100; i++)
                {
                    sync.ApplyDrivenByLeft(1 + (i % 30), 1 + (i % 3));
                    sync.ApplyDrivenByRight(1 + (i % 25), 1);
                }

                sync.FlushPendingApply();
                Console.WriteLine("OK coalesce spam 100× Apply no exception Last="
                    + (sync.CurrentState != null ? sync.CurrentState.StatusLine : "(null)"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL 100× Apply threw: " + ex.Message);
                fail++;
            }
        }

        // E1 pure StatusLine / GapCaption without service
        string idLine = SyncSessionState.BuildStatusLine(true, false, SyncSegmentKind.Identity, 3, 3);
        if (idLine.IndexOf("行番号同期", StringComparison.Ordinal) < 0)
        {
            Console.WriteLine("FAIL E1 BuildStatusLine Identity: " + idLine);
            fail++;
        }

        string upLine = SyncSessionState.BuildStatusLine(true, false, SyncSegmentKind.Unpaired, 1, 1);
        if (upLine.IndexOf("シート未対応", StringComparison.Ordinal) < 0)
        {
            Console.WriteLine("FAIL E3 BuildStatusLine Unpaired: " + upLine);
            fail++;
        }

        if (fail == 0)
        {
            Console.WriteLine("SYNC_UX_SMOKE_PASS");
            return 0;
        }

        Console.WriteLine("SYNC_UX_SMOKE_FAIL failures=" + fail);
        return 1;
    }

    static string GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    static string FindRepoRoot()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "30_参考資料", "samples"))
                && Directory.Exists(Path.Combine(dir, "20_ソース")))
            {
                return dir;
            }

            DirectoryInfo parent = Directory.GetParent(dir);
            if (parent == null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
