#Requires -Version 5.1
<#
.SYNOPSIS
  DiffXL marathon regression: full_feature + large_image for 3+ hours.
  Alternates sample pairs, records each run, kills orphan Excel between runs.
#>
param(
  [int]$MinMinutes = 180,
  [int]$PauseSecBetweenRuns = 25,
  [string]$EvidenceRoot = ""
)

$ErrorActionPreference = 'Continue'
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not (Test-Path (Join-Path $RepoRoot '40_リリース\DiffXL.exe'))) {
  $RepoRoot = 'C:\JUN\WORK\DiffXL'
}

$Exe = Join-Path $RepoRoot '40_リリース\DiffXL.exe'
$Samples = Join-Path $RepoRoot '30_参考資料\samples'
$FullLeft = Join-Path $Samples 'full_feature_left.xlsx'
$FullRight = Join-Path $Samples 'full_feature_right.xlsx'
$LargeLeft = Join-Path $Samples 'large_image_left.xlsx'
$LargeRight = Join-Path $Samples 'large_image_right.xlsx'
$LogDir = Join-Path $env:APPDATA 'DiffXL\logs'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $EvidenceRoot = Join-Path $PSScriptRoot ("エビデンス_marathon_{0}" -f $stamp)
}
New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
$RunsDir = Join-Path $EvidenceRoot 'runs'
New-Item -ItemType Directory -Force -Path $RunsDir | Out-Null
$MasterLog = Join-Path $EvidenceRoot 'marathon-log.txt'
$SummaryMd = Join-Path $EvidenceRoot 'marathon-summary.md'
$StatusJson = Join-Path $EvidenceRoot 'status.json'

function Write-M([string]$msg) {
  $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
  Add-Content -Path $MasterLog -Value $line -Encoding UTF8
  Write-Host $line
}

function Stop-DiffXlTree {
  Get-Process -Name DiffXL -ErrorAction SilentlyContinue | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch {}
  }
  Start-Sleep -Seconds 2
  # Orphan Excel left by crashed COM embeds
  Get-Process -Name EXCEL -ErrorAction SilentlyContinue | ForEach-Object {
    try {
      $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine
      # Only kill if no visible main window (headless orphan) OR started recently by our tests
      if (-not $_.MainWindowHandle -or $_.MainWindowHandle -eq [IntPtr]::Zero) {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
      }
    } catch {}
  }
  Start-Sleep -Seconds 1
}

function Invoke-AutoLive {
  param(
    [string]$Label,
    [string]$Left,
    [string]$Right,
    [int]$TimeoutSec
  )

  $runId = "{0:yyyyMMdd_HHmmss}_{1}" -f (Get-Date), ($Label -replace '[^\w\-]+','_')
  $runDir = Join-Path $RunsDir $runId
  New-Item -ItemType Directory -Force -Path $runDir | Out-Null
  $report = Join-Path $runDir 'auto-live-report.txt'
  $stdout = Join-Path $runDir 'stdout.txt'
  $meta = Join-Path $runDir 'meta.txt'

  if (-not (Test-Path $Left) -or -not (Test-Path $Right)) {
    Write-M "SKIP $Label missing samples"
    return [pscustomobject]@{
      Label=$Label; RunId=$runId; ExitCode=99; Pass=$false; DurationSec=0;
      Report=$report; Snippet='missing samples'; ImageCount=-1; TotalCount=-1
    }
  }

  Stop-DiffXlTree
  Write-M "START $Label timeout=${TimeoutSec}s left=$(Split-Path $Left -Leaf)"
  $sw = [System.Diagnostics.Stopwatch]::StartNew()

  $argList = @(
    '--auto-live-test',
    '--left', $Left,
    '--right', $Right,
    '--report', $report
  )

  $proc = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru -WindowStyle Minimized
  $exited = $proc.WaitForExit($TimeoutSec * 1000)
  if (-not $exited) {
    Write-M "TIMEOUT $Label after ${TimeoutSec}s — killing"
    try { Stop-Process -Id $proc.Id -Force } catch {}
    Stop-DiffXlTree
    $sw.Stop()
    "TIMEOUT after ${TimeoutSec}s" | Set-Content $meta -Encoding UTF8
    return [pscustomobject]@{
      Label=$Label; RunId=$runId; ExitCode=124; Pass=$false; DurationSec=[int]$sw.Elapsed.TotalSeconds;
      Report=$report; Snippet='TIMEOUT'; ImageCount=-1; TotalCount=-1
    }
  }

  $sw.Stop()
  $code = $proc.ExitCode
  $text = if (Test-Path $report) { Get-Content $report -Raw -ErrorAction SilentlyContinue } else { '' }
  $pass = ($code -eq 0) -and ($text -match 'AUTO_LIVE_PASS') -and ($text -notmatch 'AUTO_LIVE_FAIL')
  $total = -1; $img = -1
  # 新旧フォーマット両対応:
  #   COMPARE_OK count=N text=T image=I structure=S
  #   COMPARE_OK count=N text=T image=I imageOnlyL=.. imageOnlyR=.. structure=S elapsedMs=..
  if ($text -match 'COMPARE_OK count=(\d+)\s+text=(\d+)\s+image=(\d+)') {
    $total = [int]$Matches[1]
    $img = [int]$Matches[3]
  }

  # App log tail
  $latestLog = Get-ChildItem $LogDir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($latestLog) {
    Get-Content $latestLog.FullName -Tail 80 -ErrorAction SilentlyContinue | Set-Content (Join-Path $runDir 'app-log-tail.txt') -Encoding UTF8
  }

  @(
    "Label=$Label"
    "ExitCode=$code"
    "Pass=$pass"
    "DurationSec=$([int]$sw.Elapsed.TotalSeconds)"
    "TotalCount=$total"
    "ImageCount=$img"
  ) | Set-Content $meta -Encoding UTF8

  Write-M ("END {0} exit={1} pass={2} dur={3}s count={4} image={5}" -f $Label, $code, $pass, [int]$sw.Elapsed.TotalSeconds, $total, $img)
  Stop-DiffXlTree

  return [pscustomobject]@{
    Label=$Label; RunId=$runId; ExitCode=$code; Pass=$pass;
    DurationSec=[int]$sw.Elapsed.TotalSeconds; Report=$report;
    Snippet= if ($text.Length -gt 300) { $text.Substring([Math]::Max(0,$text.Length-300)) } else { $text };
    ImageCount=$img; TotalCount=$total
  }
}

# --- main ---
$start = Get-Date
$deadline = $start.AddMinutes($MinMinutes)
Write-M "Marathon BEGIN minMinutes=$MinMinutes deadline=$deadline"
Write-M "Exe=$Exe"
Write-M "Evidence=$EvidenceRoot"

$results = New-Object System.Collections.Generic.List[object]
$round = 0
$failCount = 0
$passCount = 0

while ((Get-Date) -lt $deadline) {
  $round++
  $elapsedMin = [int]((Get-Date) - $start).TotalMinutes
  Write-M "=== ROUND $round elapsed=${elapsedMin}m remaining=$([int]($deadline - (Get-Date)).TotalMinutes)m ==="

  # 1) full_feature (baseline, shorter timeout)
  $r1 = Invoke-AutoLive -Label "full_feature" -Left $FullLeft -Right $FullRight -TimeoutSec 240
  $results.Add($r1)
  if ($r1.Pass) { $passCount++ } else { $failCount++ }

  if ((Get-Date) -ge $deadline) { break }
  Start-Sleep -Seconds $PauseSecBetweenRuns

  # 2) large_image (long timeout: OpenCV on multi-MB images)
  $r2 = Invoke-AutoLive -Label "large_image" -Left $LargeLeft -Right $LargeRight -TimeoutSec 600
  $results.Add($r2)
  if ($r2.Pass) { $passCount++ } else { $failCount++ }

  # status snapshot for external monitors
  @{
    start = $start.ToString('o')
    deadline = $deadline.ToString('o')
    now = (Get-Date).ToString('o')
    round = $round
    pass = $passCount
    fail = $failCount
    lastFull = $r1.Pass
    lastLarge = $r2.Pass
    lastLargeCount = $r2.TotalCount
    lastLargeImage = $r2.ImageCount
    lastLargeSec = $r2.DurationSec
  } | ConvertTo-Json | Set-Content $StatusJson -Encoding UTF8

  if ((Get-Date) -ge $deadline) { break }
  Start-Sleep -Seconds $PauseSecBetweenRuns
}

$end = Get-Date
$totalMin = [Math]::Round(($end - $start).TotalMinutes, 1)
Write-M "Marathon END rounds=$round pass=$passCount fail=$failCount totalMin=$totalMin"

# Summary markdown
$md = @()
$md += "# DiffXL Marathon Test Summary"
$md += ""
$md += "| 項目 | 値 |"
$md += "|------|-----|"
$md += "| 開始 | $($start.ToString('yyyy-MM-dd HH:mm:ss')) |"
$md += "| 終了 | $($end.ToString('yyyy-MM-dd HH:mm:ss')) |"
$md += "| 所要 | ${totalMin} 分（目標 ${MinMinutes}+） |"
$md += "| ラウンド | $round |"
$md += "| PASS 実行 | $passCount |"
$md += "| FAIL 実行 | $failCount |"
$md += "| サンプル | full_feature + large_image |"
$md += ""
$md += "## 実行一覧"
$md += ""
$md += "| # | Label | Pass | Exit | Sec | Count | Image | RunId |"
$md += "|---|-------|------|------|-----|-------|-------|-------|"
$i = 0
foreach ($r in $results) {
  $i++
  $md += ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | `{7}` |" -f $i, $r.Label, $r.Pass, $r.ExitCode, $r.DurationSec, $r.TotalCount, $r.ImageCount, $r.RunId)
}
$md += ""
$md += "## 合否"
if ($failCount -eq 0 -and $passCount -gt 0) {
  $md += "- **ALL PASS**（FAIL=0）"
} else {
  $md += "- **FAILURES PRESENT** fail=$failCount / pass=$passCount"
  $md += "- 失敗ランの report / app-log-tail を `runs/` 配下で確認"
}
$md += ""
$md += "## large_image 期待"
$md += "- 画像差分: BIG-B, BIG-C(left-only), BIG-D(right-only), BIG-F, BIG-H など image>0"
$md += "- MiniMap: MINIMAP_OK / MINIMAP_MULTI_OK"
$md += "- タイムアウトなし（600s 以内）"
$md -join "`n" | Set-Content $SummaryMd -Encoding UTF8

Write-M "Wrote $SummaryMd"
Write-Host "EVIDENCE=$EvidenceRoot"
exit $(if ($failCount -gt 0) { 1 } else { 0 })
