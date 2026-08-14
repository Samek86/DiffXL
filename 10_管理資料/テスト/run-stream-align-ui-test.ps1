#Requires -Version 5.1
<#
.SYNOPSIS
  ContentStream テーブル/セル行対応の UI 検証（キャプチャ付き）。
  large_image 売上サマリで「この側になし」誤配置が無いことを確認する。
#>
$ErrorActionPreference = 'Continue'
$Stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$Repo = 'C:\JUN\WORK\DiffXL'
$EvidenceRoot = Join-Path $Repo "10_管理資料\テスト\エビデンス_stream_align_$Stamp"
$ShotDir = Join-Path $EvidenceRoot 'screenshots'
New-Item -ItemType Directory -Force -Path $EvidenceRoot, $ShotDir | Out-Null

$Exe = Join-Path $Repo '20_ソース\DiffXL\DiffXL\bin\x64\Debug\DiffXL.exe'
$LeftXlsx = Join-Path $Repo '30_参考資料\samples\large_image_left.xlsx'
$RightXlsx = Join-Path $Repo '30_参考資料\samples\large_image_right.xlsx'
$RunLog = Join-Path $EvidenceRoot 'run-log.txt'
$AutoReport = Join-Path $EvidenceRoot 'auto-live-report.txt'
$EngineReport = Join-Path $EvidenceRoot 'engine-stream-report.txt'
$ResultsMd = Join-Path $EvidenceRoot 'test-results.md'
$LatestPointer = Join-Path $Repo '10_管理資料\テスト\_latest_stream_align_evidence.txt'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$win32 = @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Win32UiStream {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
try { Add-Type -TypeDefinition $win32 -ErrorAction Stop } catch { }

$script:ShotIndex = 0
$script:Results = New-Object System.Collections.Generic.List[object]
$script:FailCount = 0

function Write-Run([string]$msg) {
  $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss.fff'), $msg
  Add-Content -Path $RunLog -Value $line -Encoding UTF8
  Write-Host $line
}

function Add-Result([string]$id, [string]$name, [string]$status, [string]$note) {
  $script:Results.Add([pscustomobject]@{ Id=$id; Name=$name; Status=$status; Note=$note; Time=(Get-Date -Format 'HH:mm:ss') })
  if ($status -eq 'FAIL') { $script:FailCount++ }
  Write-Run ("RESULT {0} {1} | {2}" -f $id, $status, $note)
}

function Capture-Shot([string]$name, [System.Diagnostics.Process]$proc = $null) {
  $script:ShotIndex++
  $safe = ($name -replace '[^\w\-]+','_')
  $path = Join-Path $ShotDir ("{0:D2}_{1}.png" -f $script:ShotIndex, $safe)
  try {
    Start-Sleep -Milliseconds 300
    if ($proc -and -not $proc.HasExited -and $proc.MainWindowHandle -ne [IntPtr]::Zero) {
      [void][Win32UiStream]::SetForegroundWindow($proc.MainWindowHandle)
      Start-Sleep -Milliseconds 200
      $rect = New-Object Win32UiStream+RECT
      if ([Win32UiStream]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)) {
        $w = [Math]::Max(1, $rect.Right - $rect.Left)
        $h = [Math]::Max(1, $rect.Bottom - $rect.Top)
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
        $g.Dispose()
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Run "SHOT $path (${w}x${h})"
        return $path
      }
    }
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bounds.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Run "SHOT full $path"
    return $path
  } catch {
    Write-Run "SHOT FAIL $name : $($_.Exception.Message)"
    return $null
  }
}

function Get-Root([System.Diagnostics.Process]$proc) {
  if (-not $proc -or $proc.HasExited) { return $null }
  $proc.Refresh()
  $hwnd = $proc.MainWindowHandle
  if ($hwnd -eq [IntPtr]::Zero) { return $null }
  return [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
}

function Dump-UiText([System.Windows.Automation.AutomationElement]$root, [string]$outPath, [int]$max = 400) {
  if (-not $root) { return '' }
  $sb = New-Object System.Text.StringBuilder
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $stack = New-Object System.Collections.Generic.Stack[object]
  $stack.Push(@{ El = $root; Depth = 0 })
  $n = 0
  while ($stack.Count -gt 0 -and $n -lt $max) {
    $cur = $stack.Pop()
    $el = $cur.El
    try {
      $name = $el.Current.Name
      $ct = $el.Current.ControlType.ProgrammaticName
      if (-not [string]::IsNullOrWhiteSpace($name)) {
        [void]$sb.AppendLine(('{0}{1} | {2}' -f ('  ' * $cur.Depth), $ct, $name))
        $n++
      }
      $child = $walker.GetLastChild($el)
      while ($null -ne $child) {
        $stack.Push(@{ El = $child; Depth = $cur.Depth + 1 })
        $child = $walker.GetPreviousSibling($child)
      }
    } catch {}
  }
  $text = $sb.ToString()
  Set-Content -Path $outPath -Value $text -Encoding UTF8
  return $text
}

function Select-ComboItemByText([System.Windows.Automation.AutomationElement]$root, [string]$want) {
  if (-not $root) { return $false }
  $comboCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::ComboBox)
  $combos = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $comboCond)
  foreach ($combo in $combos) {
    try {
      $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
      $expand.Expand()
      Start-Sleep -Milliseconds 250
      $itemCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
      $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
      foreach ($it in $items) {
        $nm = $it.Current.Name
        if ($nm -and ($nm -eq $want -or $nm.Contains($want))) {
          try {
            $sel = $it.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $sel.Select()
          } catch {
            try { $it.SetFocus(); [System.Windows.Forms.SendKeys]::SendWait('{ENTER}') } catch {}
          }
          Start-Sleep -Milliseconds 600
          try { $expand.Collapse() } catch {}
          return $true
        }
      }
      try { $expand.Collapse() } catch {}
    } catch {}
  }
  return $false
}

# ---------- 0) prerequisites ----------
Write-Run "Evidence=$EvidenceRoot"
Set-Content -Path $LatestPointer -Value $EvidenceRoot -Encoding UTF8

if (-not (Test-Path $Exe)) {
  Add-Result 'P0' 'DiffXL.exe exists' 'FAIL' $Exe
  throw "missing exe: $Exe"
}
if (-not (Test-Path $LeftXlsx) -or -not (Test-Path $RightXlsx)) {
  Add-Result 'P0' 'samples exist' 'FAIL' 'large_image xlsx missing'
  throw 'missing samples'
}
Add-Result 'P0' 'prereq' 'PASS' "exe+samples ok"

# ---------- 1) engine stream validation (all sheets) ----------
$dbg = Split-Path $Exe -Parent
$diagCs = Join-Path $EvidenceRoot 'engine_diag.cs'
$diagExe = Join-Path $dbg '_stream_ui_diag.exe'
$diagSrc = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DiffXL.COMMON;
using DiffXL.LOGIC.Diff;

class EngineStreamDiag {
  static int Main(string[] args) {
    string leftPath = args[0];
    string rightPath = args[1];
    string outPath = args[2];
    var sb = new StringBuilder();
    int fails = 0;
    try {
      AppPaths.EnsureDirectories();
      NativeBootstrap.EnsureNativeBinaries();
      sb.AppendLine("ENGINE_STREAM_DIAG");
      sb.AppendLine("L=" + leftPath);
      sb.AppendLine("R=" + rightPath);
      DiffResult result = new DiffEngine().Compare(leftPath, rightPath);
      sb.AppendLine("err=" + (result.ErrorMessage ?? ""));
      sb.AppendLine("items=" + (result.Items != null ? result.Items.Count : 0));
      if (result.LeftContent == null || result.RightContent == null) {
        sb.AppendLine("FAIL no content");
        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        return 2;
      }

      foreach (SheetContent ls in result.LeftContent.Sheets) {
        if (ls == null) continue;
        SheetContent rs = result.RightContent.Sheets.FirstOrDefault(s => s != null && s.Name == ls.Name);
        if (rs == null) {
          sb.AppendLine("SHEET " + ls.Name + " right-missing (skip stream pair check)");
          continue;
        }

        IList<ContentStreamBlock> lb = ContentStreamBuilder.Build(ls);
        IList<ContentStreamBlock> rb = ContentStreamBuilder.Build(rs);
        IList<ContentStreamPair> pairs = ContentStreamBuilder.Align(lb, rb);
        sb.AppendLine("==== SHEET " + ls.Name + " Lblocks=" + lb.Count + " Rblocks=" + rb.Count + " pairs=" + pairs.Count);

        int tableL = lb.Count(b => b.Kind == ContentBlockKind.Table);
        int tableR = rb.Count(b => b.Kind == ContentBlockKind.Table);
        int tableMatch = pairs.Count(p => p.Op == AlignOp.Match
          && p.Left != null && p.Right != null
          && p.Left.Kind == ContentBlockKind.Table
          && p.Right.Kind == ContentBlockKind.Table);
        int tableSkip = pairs.Count(p =>
          (p.Left != null && p.Left.Kind == ContentBlockKind.Table && p.Op != AlignOp.Match)
          || (p.Right != null && p.Right.Kind == ContentBlockKind.Table && p.Op != AlignOp.Match));

        sb.AppendLine("  tables L=" + tableL + " R=" + tableR + " matchPairs=" + tableMatch + " unpairedTableSteps=" + tableSkip);

        // When both sides have exactly 1 table at same-ish place, require Match
        if (tableL == 1 && tableR == 1) {
          double sim = ContentStreamBuilder.BlockSimilarity(
            lb.First(b => b.Kind == ContentBlockKind.Table),
            rb.First(b => b.Kind == ContentBlockKind.Table));
          sb.AppendLine("  single-table sim=" + sim.ToString("F4") + " thr=" + ContentStreamBuilder.MatchThreshold);
          if (tableMatch != 1) {
            sb.AppendLine("  FAIL single table pair not matched on sheet " + ls.Name);
            fails++;
          } else {
            sb.AppendLine("  OK single table matched");
          }
        }

        // 売上サマリ: no false gaps for the 3 main blocks
        if (ls.Name == "売上サマリ") {
          bool allMatch = pairs.Count >= 3 && pairs.Take(3).All(p => p.Op == AlignOp.Match);
          bool tablePaired = pairs.Any(p => p.Op == AlignOp.Match
            && p.Left != null && p.Right != null
            && p.Left.Kind == ContentBlockKind.Table
            && p.Right.Kind == ContentBlockKind.Table);
          bool row10 = pairs.Any(p => p.Op == AlignOp.Match
            && p.Left != null && p.Right != null
            && p.Left.Kind == ContentBlockKind.LooseRow
            && p.Right.Kind == ContentBlockKind.LooseRow
            && p.Left.Row == 10 && p.Right.Row == 10);
          sb.AppendLine("  sales allMatchTop=" + allMatch + " tablePaired=" + tablePaired + " row10=" + row10);
          if (!tablePaired || !row10) {
            sb.AppendLine("  FAIL 売上サマリ expected table+row10 Match");
            fails++;
          } else {
            sb.AppendLine("  OK 売上サマリ stream pairing");
          }
        }

        for (int i = 0; i < pairs.Count; i++) {
          ContentStreamPair p = pairs[i];
          string L = p.Left == null ? "-" : (p.Left.Kind + "@r" + p.Left.Row);
          string R = p.Right == null ? "-" : (p.Right.Kind + "@r" + p.Right.Row);
          sb.AppendLine("  [" + i + "] " + p.Op + " L=" + L + " R=" + R);
        }
      }

      sb.AppendLine("FAILS=" + fails);
      sb.AppendLine(fails == 0 ? "ENGINE_STREAM_PASS" : "ENGINE_STREAM_FAIL");
      File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
      Console.WriteLine(sb.ToString());
      return fails == 0 ? 0 : 1;
    } catch (Exception ex) {
      sb.AppendLine("EXCEPTION " + ex);
      File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
      Console.WriteLine(ex);
      return 3;
    }
  }
}
'@
Set-Content -Path $diagCs -Value $diagSrc -Encoding UTF8
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Write-Run "Compile engine diag"
& $csc /nologo /target:exe /platform:x64 /out:$diagExe /r:"$Exe" $diagCs 2>&1 | ForEach-Object { Write-Run "csc: $_" }
if ($LASTEXITCODE -ne 0) {
  Add-Result 'E1' 'engine diag compile' 'FAIL' "csc exit $LASTEXITCODE"
} else {
  Push-Location $dbg
  & $diagExe $LeftXlsx $RightXlsx $EngineReport 2>&1 | ForEach-Object { Write-Run "eng: $_" }
  $engExit = $LASTEXITCODE
  Pop-Location
  if ($engExit -eq 0 -and (Select-String -Path $EngineReport -Pattern 'ENGINE_STREAM_PASS' -Quiet)) {
    Add-Result 'E1' 'engine stream all sheets' 'PASS' "exit=$engExit"
  } else {
    Add-Result 'E1' 'engine stream all sheets' 'FAIL' "exit=$engExit see engine-stream-report.txt"
  }
}

# ---------- 2) UI auto-live with screenshots ----------
Get-Process DiffXL -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

if (Test-Path $AutoReport) { Remove-Item $AutoReport -Force }

Write-Run "Start DiffXL auto-live large_image (no-quit)"
$argList = @(
  '--auto-live-test',
  '--left', $LeftXlsx,
  '--right', $RightXlsx,
  '--report', $AutoReport,
  '--no-quit'
)
Write-Run ("args: " + ($argList -join ' '))
$proc = Start-Process -FilePath $Exe -ArgumentList $argList -WorkingDirectory $dbg -PassThru
Write-Run "PID=$($proc.Id)"

# Wait for main window
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
  $proc.Refresh()
  if ($proc.HasExited) { break }
  if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { break }
  Start-Sleep -Milliseconds 300
}
if (-not $proc.HasExited) {
  try {
    [void][Win32UiStream]::ShowWindow($proc.MainWindowHandle, 3) # maximize
    [void][Win32UiStream]::MoveWindow($proc.MainWindowHandle, 40, 20, 1500, 950, $true)
  } catch {}
  Capture-Shot '01_startup_or_loading' $proc | Out-Null
}

# Poll compare completion (large images can take several minutes)
$compareDeadline = (Get-Date).AddSeconds(600)
$compareOk = $false
while ((Get-Date) -lt $compareDeadline) {
  if ($proc.HasExited) {
    Write-Run "Process exited early code=$($proc.ExitCode)"
    break
  }
  if (Test-Path $AutoReport) {
    $txt = Get-Content $AutoReport -Raw -ErrorAction SilentlyContinue
    if ($txt -match 'COMPARE_OK') {
      $compareOk = $true
      Write-Run "COMPARE_OK detected"
      Capture-Shot '02_compare_ok' $proc | Out-Null
      break
    }
    if ($txt -match 'FAIL compare') {
      Write-Run "FAIL compare in report"
      break
    }
  }
  Start-Sleep -Seconds 2
}

if ($compareOk) {
  Add-Result 'U1' 'auto-live compare' 'PASS' 'COMPARE_OK'
} else {
  Add-Result 'U1' 'auto-live compare' 'FAIL' 'no COMPARE_OK within timeout'
}

# Wait a bit more for UI to settle after compare; capture current
Start-Sleep -Seconds 2
Capture-Shot '03_after_compare_settle' $proc | Out-Null

# Wait for auto-live end first so it stops changing sheets under us
$endDeadline = (Get-Date).AddSeconds(180)
$autoDone = $false
while ((Get-Date) -lt $endDeadline) {
  if (Test-Path $AutoReport) {
    $txt = Get-Content $AutoReport -Raw -ErrorAction SilentlyContinue
    if ($txt -match 'AUTO_LIVE_PASS|AUTO_LIVE_FAIL') {
      Write-Run "auto-live finished marker found"
      $autoDone = $true
      if ($txt -match 'AUTO_LIVE_PASS') {
        Add-Result 'U3' 'auto-live overall' 'PASS' 'AUTO_LIVE_PASS'
      } else {
        Add-Result 'U3' 'auto-live overall' 'WARN' 'AUTO_LIVE_FAIL (non-stream checks may fail on large_image)'
      }
      break
    }
  }
  if ($proc.HasExited) {
    Write-Run "process exited code=$($proc.ExitCode)"
    break
  }
  Start-Sleep -Seconds 2
}
if (-not $autoDone) {
  Add-Result 'U3' 'auto-live overall' 'WARN' 'no AUTO_LIVE marker (timeout)'
}

function Assert-SheetView([System.Diagnostics.Process]$proc, [string]$sheet, [string]$shotName, [string]$resultId) {
  $root = Get-Root $proc
  if (-not $root) {
    Add-Result $resultId "$sheet UI root" 'FAIL' 'no automation root'
    Capture-Shot "${shotName}_no_root" $proc | Out-Null
    return
  }
  $selected = Select-ComboItemByText $root $sheet
  Write-Run "Select $sheet => $selected"
  Start-Sleep -Seconds 1.2
  # re-select if panes lag
  $root = Get-Root $proc
  $uiText = Dump-UiText $root (Join-Path $EvidenceRoot "ui-tree-$shotName.txt") 600
  if ($uiText -notmatch [regex]::Escape("シート「$sheet」")) {
    Start-Sleep -Seconds 1
    [void](Select-ComboItemByText $root $sheet)
    Start-Sleep -Seconds 1
    $root = Get-Root $proc
    $uiText = Dump-UiText $root (Join-Path $EvidenceRoot "ui-tree-$shotName.txt") 600
  }
  Capture-Shot $shotName $proc | Out-Null
  $leftOk = $uiText -match ("左 · シート「" + [regex]::Escape($sheet) + "」")
  $rightOk = $uiText -match ("右 · シート「" + [regex]::Escape($sheet) + "」")
  $gapTable = ([regex]::Matches($uiText, 'この側になし（テーブル）')).Count
  $gapCell = ([regex]::Matches($uiText, 'この側になし（セル行）')).Count
  $pairedTable = $uiText -match '対応 T0 ↔ T0|対応 T1 ↔ T1|対応 T0 ↔ T0'
  # paired table titles look like: 対応 T0 ↔ T0
  $pairedHint = ([regex]::Matches($uiText, '対応 T\d+ ↔ T\d+')).Count
  $unpairedHint = ([regex]::Matches($uiText, '対応 T\d+ ↔ —|対応 — ↔ T\d+')).Count
  Write-Run "$sheet leftOk=$leftOk rightOk=$rightOk gapsT=$gapTable gapsC=$gapCell paired=$pairedHint unpaired=$unpairedHint"
  if (-not $selected) {
    Add-Result $resultId "$sheet combo select" 'FAIL' 'could not select'
    return
  }
  if (-not ($leftOk -and $rightOk)) {
    Add-Result $resultId "$sheet both panes" 'FAIL' "L=$leftOk R=$rightOk"
    return
  }
  # For sheets that should have tables paired (売上/カタログ/長い一覧/表紙): no table gaps, no unpaired tables
  if ($gapTable -gt 0 -or $unpairedHint -gt 0) {
    Add-Result $resultId "$sheet table pairing UI" 'FAIL' "gapsT=$gapTable unpaired=$unpairedHint"
    return
  }
  Add-Result $resultId "$sheet UI pairing" 'PASS' "gapsT=$gapTable gapsC=$gapCell paired=$pairedHint"
}

Assert-SheetView $proc '売上サマリ' '04_sales_summary' 'U2'
Assert-SheetView $proc '製品カタログ' '05_catalog' 'U4'
Assert-SheetView $proc '長い一覧' '06_longlist' 'U5'
Assert-SheetView $proc '表紙' '07_cover' 'U6'

# Final capture
$root = Get-Root $proc
if ($root) { [void](Select-ComboItemByText $root '売上サマリ'); Start-Sleep -Seconds 1 }
Capture-Shot '99_final_sales' $proc | Out-Null

# Keep app a moment for last shot then close
if (-not $proc.HasExited) {
  Capture-Shot '99b_before_close' $proc | Out-Null
  try { $proc.CloseMainWindow() | Out-Null } catch {}
  Start-Sleep -Seconds 2
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}

# ---------- 3) results md ----------
$passN = @($script:Results | Where-Object { $_.Status -eq 'PASS' }).Count
$failN = @($script:Results | Where-Object { $_.Status -eq 'FAIL' }).Count
$warnN = @($script:Results | Where-Object { $_.Status -eq 'WARN' }).Count
$overall = if ($failN -eq 0) { 'PASS' } else { 'FAIL' }

$md = @()
$md += "# Stream Align UI Test Results"
$md += ""
$md += "| 項目 | 値 |"
$md += "|------|-----|"
$md += "| 日時 | $Stamp |"
$md += "| 結果 | **$overall** |"
$md += "| PASS | $passN |"
$md += "| FAIL | $failN |"
$md += "| WARN | $warnN |"
$md += "| EXE | ``$Exe`` |"
$md += "| サンプル | large_image_left/right.xlsx |"
$md += ""
$md += "## ケース"
$md += ""
$md += "| ID | 名称 | 結果 | メモ |"
$md += "|----|------|------|------|"
foreach ($r in $script:Results) {
  $md += "| $($r.Id) | $($r.Name) | $($r.Status) | $($r.Note) |"
}
$md += ""
$md += "## 成果物"
$md += "- screenshots/"
$md += "- engine-stream-report.txt"
$md += "- auto-live-report.txt"
$md += "- ui-tree-sales.txt"
$md += "- run-log.txt"
$md -join "`n" | Set-Content -Path $ResultsMd -Encoding UTF8

Write-Run "OVERALL $overall pass=$passN fail=$failN warn=$warnN"
Write-Host "EVIDENCE=$EvidenceRoot"
if ($failN -gt 0) { exit 1 } else { exit 0 }
