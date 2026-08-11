#Requires -Version 5.1
<#
.SYNOPSIS
  DiffXL UX sync harness: SyncUxSmoke + PerfectSmoke + content_scroll/full_feature auto-live.

.DESCRIPTION
  1. msbuild DiffXL Debug|x64
  2. Compile & run SyncUxSmoke → SYNC_UX_SMOKE_PASS
  3. Compile & run ContentScrollPerfectSmoke → PERFECT_SCROLL_PASS
  4. DiffXL --auto-live-test content_scroll → AUTO_LIVE_PASS (+ Status 検証ログ)
  5. DiffXL --auto-live-test full_feature → AUTO_LIVE_PASS
  6. Copy reports into エビデンス_ux_sync_yyyyMMdd_HHmmss/
#>
param(
  [switch]$SkipBuild,
  [switch]$SkipLive,
  [switch]$SkipFullFeature,
  [int]$LiveTimeoutSec = 180,
  [string]$EvidenceRoot = ""
)

$ErrorActionPreference = 'Continue'
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not (Test-Path (Join-Path $RepoRoot '30_参考資料\samples\content_scroll_left.xlsx'))) {
  $RepoRoot = 'C:\JUN\WORK\DiffXL'
}

$SrcRoot = Join-Path $RepoRoot '20_ソース\DiffXL'
$ProjDir = Join-Path $SrcRoot 'DiffXL'
$BinDir = Join-Path $ProjDir 'bin\x64\Debug'
$SmokeDir = Join-Path $SrcRoot '_smoke'
$SyncUxSrc = Join-Path $SmokeDir 'SyncUxSmoke.cs'
$PerfectSrc = Join-Path $SmokeDir 'ContentScrollPerfectSmoke.cs'
$Samples = Join-Path $RepoRoot '30_参考資料\samples'
$CsLeft = Join-Path $Samples 'content_scroll_left.xlsx'
$CsRight = Join-Path $Samples 'content_scroll_right.xlsx'
$CsExpected = Join-Path $Samples 'content_scroll_expected.json'
$FfLeft = Join-Path $Samples 'full_feature_left.xlsx'
$FfRight = Join-Path $Samples 'full_feature_right.xlsx'
$LatestCs = Join-Path $PSScriptRoot '_latest_ux_content_scroll.txt'
$LatestFf = Join-Path $PSScriptRoot '_latest_ux_full_feature.txt'
$Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $EvidenceRoot = Join-Path $PSScriptRoot ("エビデンス_ux_sync_{0}" -f $stamp)
}
New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
$RunLog = Join-Path $EvidenceRoot 'run-log.txt'

function Write-R([string]$msg) {
  $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
  Add-Content -Path $RunLog -Value $line -Encoding UTF8
  Write-Host $line
}

function Find-MSBuild {
  $candidates = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\MSBuild\14.0\Bin\MSBuild.exe"
  )
  foreach ($c in $candidates) {
    if ($c -and (Test-Path $c)) { return $c }
  }
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  if (Test-Path $vswhere) {
    $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null
    if ($found) {
      if ($found -is [array]) { return $found[0] }
      return $found
    }
  }
  return $null
}

function Stop-DiffXL {
  Get-Process -Name DiffXL -ErrorAction SilentlyContinue | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch {}
  }
  Start-Sleep -Seconds 1
}

function Invoke-AutoLive {
  param(
    [string]$Left,
    [string]$Right,
    [string]$ReportPath,
    [string]$EvidenceName,
    [string]$Label
  )
  if (Test-Path $ReportPath) { Remove-Item -Force $ReportPath -ErrorAction SilentlyContinue }
  Stop-DiffXL
  Write-R "RUN auto-live $Label timeout=${LiveTimeoutSec}s"
  $argList = @(
    '--auto-live-test',
    '--left', $Left,
    '--right', $Right,
    '--report', $ReportPath
  )
  $proc = Start-Process -FilePath $exe -ArgumentList $argList -PassThru -WindowStyle Minimized
  $exited = $proc.WaitForExit($LiveTimeoutSec * 1000)
  if (-not $exited) {
    Write-R "TIMEOUT auto-live $Label after ${LiveTimeoutSec}s"
    try { Stop-Process -Id $proc.Id -Force } catch {}
    return $false
  }
  Write-R "auto-live $Label exit=$($proc.ExitCode)"
  $ok = $true
  if ($proc.ExitCode -ne 0) { $ok = $false }
  if (Test-Path $ReportPath) {
    Copy-Item -Force $ReportPath (Join-Path $EvidenceRoot $EvidenceName)
    $reportText = Get-Content -Path $ReportPath -Raw -ErrorAction SilentlyContinue
    if ($reportText -match 'AUTO_LIVE_PASS' -and $reportText -match 'FAILURES=0') {
      Write-R "PASS $Label AUTO_LIVE_PASS"
    } else {
      Write-R "FAIL $Label auto-live (see $EvidenceName)"
      $ok = $false
    }
  } else {
    Write-R "FAIL $Label report missing: $ReportPath"
    $ok = $false
  }
  return $ok
}

$overallFail = 0
Write-R "BEGIN ux-sync harness RepoRoot=$RepoRoot"
Write-R "Evidence=$EvidenceRoot"

foreach ($p in @($CsLeft, $CsRight, $CsExpected, $FfLeft, $FfRight, $SyncUxSrc, $PerfectSrc)) {
  if (-not (Test-Path $p)) {
    Write-R "FAIL missing: $p"
    $overallFail++
  }
}
if ($overallFail -gt 0) {
  Write-R "ABORT missing inputs"
  exit 2
}

# ---- 1. Build ----
if (-not $SkipBuild) {
  $msbuild = Find-MSBuild
  if (-not $msbuild) {
    Write-R "FAIL MSBuild not found"
    exit 3
  }
  Write-R "MSBuild=$msbuild"
  $sln = Join-Path $SrcRoot 'DiffXL.sln'
  & $msbuild $sln /p:Configuration=Debug /p:Platform=x64 /v:m /nologo
  if ($LASTEXITCODE -ne 0) {
    Write-R "FAIL msbuild exit=$LASTEXITCODE"
    exit 4
  }
  Write-R "BUILD_OK"
} else {
  Write-R "BUILD skipped"
}

$exe = Join-Path $BinDir 'DiffXL.exe'
if (-not (Test-Path $exe)) {
  Write-R "FAIL DiffXL.exe missing at $exe"
  exit 5
}
if (-not (Test-Path $Csc)) {
  Write-R "FAIL csc not found: $Csc"
  exit 6
}

# ---- 2. SyncUxSmoke ----
$syncOut = Join-Path $BinDir 'SyncUxSmoke.exe'
Write-R "csc SyncUxSmoke -> $syncOut"
& $Csc /nologo /platform:x64 /optimize+ /r:"$BinDir\DiffXL.exe" /out:$syncOut $SyncUxSrc
if ($LASTEXITCODE -ne 0) {
  Write-R "FAIL csc SyncUxSmoke exit=$LASTEXITCODE"
  exit 7
}
Copy-Item -Force $syncOut (Join-Path $SmokeDir 'SyncUxSmoke.exe') -ErrorAction SilentlyContinue

$syncLog = Join-Path $EvidenceRoot 'sync-ux-smoke.txt'
Write-R "RUN SyncUxSmoke"
Push-Location $BinDir
try {
  $syncText = & .\SyncUxSmoke.exe --left $CsLeft --right $CsRight 2>&1 | Out-String
  $syncExit = $LASTEXITCODE
} finally {
  Pop-Location
}
Set-Content -Path $syncLog -Value $syncText -Encoding UTF8
Write-Host $syncText
Write-R "SyncUxSmoke exit=$syncExit"
if ($syncExit -ne 0 -or $syncText -notmatch 'SYNC_UX_SMOKE_PASS') {
  Write-R "FAIL SyncUxSmoke"
  $overallFail++
} else {
  Write-R "PASS SYNC_UX_SMOKE_PASS"
}

# ---- 3. ContentScrollPerfectSmoke ----
$perfectOut = Join-Path $BinDir 'ContentScrollPerfectSmoke.exe'
Write-R "csc PerfectSmoke -> $perfectOut"
& $Csc /nologo /platform:x64 /optimize+ /r:"$BinDir\DiffXL.exe" /out:$perfectOut $PerfectSrc
if ($LASTEXITCODE -ne 0) {
  Write-R "FAIL csc ContentScrollPerfectSmoke exit=$LASTEXITCODE"
  exit 8
}
Copy-Item -Force $perfectOut (Join-Path $SmokeDir 'ContentScrollPerfectSmoke.exe') -ErrorAction SilentlyContinue

$perfectLog = Join-Path $EvidenceRoot 'perfect-smoke.txt'
Write-R "RUN PerfectSmoke"
Push-Location $BinDir
try {
  $perfectText = & .\ContentScrollPerfectSmoke.exe --left $CsLeft --right $CsRight --expected $CsExpected 2>&1 | Out-String
  $perfectExit = $LASTEXITCODE
} finally {
  Pop-Location
}
Set-Content -Path $perfectLog -Value $perfectText -Encoding UTF8
Write-Host $perfectText
Write-R "PerfectSmoke exit=$perfectExit"
if ($perfectExit -ne 0 -or $perfectText -notmatch 'PERFECT_SCROLL_PASS') {
  Write-R "FAIL PerfectSmoke"
  $overallFail++
} else {
  Write-R "PASS PERFECT_SCROLL_PASS"
}

# ---- 4–5. Auto-live ----
if (-not $SkipLive) {
  if (-not (Invoke-AutoLive -Left $CsLeft -Right $CsRight -ReportPath $LatestCs `
      -EvidenceName 'auto-live-content_scroll.txt' -Label 'content_scroll')) {
    $overallFail++
  } else {
    # Status 検証ログ（auto-live 内 STATUS_LINE_OK / JUMP_HINT_PATH）
    $csReport = Get-Content -Path $LatestCs -Raw -ErrorAction SilentlyContinue
    if ($csReport -match 'STATUS_LINE_OK') {
      Write-R "PASS content_scroll STATUS_LINE_OK"
    } else {
      Write-R "WARN content_scroll STATUS_LINE_OK not found (older build?)"
    }
    if ($csReport -match 'JUMP_HINT_PATH') {
      Write-R "PASS content_scroll JUMP_HINT_PATH logged"
    }
  }

  if (-not $SkipFullFeature) {
    if (-not (Invoke-AutoLive -Left $FfLeft -Right $FfRight -ReportPath $LatestFf `
        -EvidenceName 'auto-live-full_feature.txt' -Label 'full_feature')) {
      $overallFail++
    }
  } else {
    Write-R "full_feature live skipped"
  }
} else {
  Write-R "LIVE skipped"
}

# ---- 6. Evidence README + checklist stub ----
$readme = @"
# ux_sync evidence

- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
- Repo: $RepoRoot
- OverallFail: $overallFail

## Stages
1. sync-ux-smoke.txt — SYNC_UX_SMOKE_PASS
2. perfect-smoke.txt — PERFECT_SCROLL_PASS
3. auto-live-content_scroll.txt — AUTO_LIVE_PASS (+ STATUS_LINE / JUMP_HINT)
4. auto-live-full_feature.txt — AUTO_LIVE_PASS

## Manual
See UXシナリオ_内容同期.md (TC-UX-01..12). Fill ux-checklist.md in this folder.

## Re-run
``````powershell
powershell -File "10_管理資料\テスト\run-ux-sync-test.ps1"
``````
"@
Set-Content -Path (Join-Path $EvidenceRoot 'README.md') -Value $readme -Encoding UTF8

$checklist = @"
# UX checklist — $(Get-Date -Format 'yyyy-MM-dd')

自動ハーネス結果: OverallFail=$overallFail（0=自動 PASS）

| ID | 結果 | メモ |
|----|------|------|
| TC-UX-01 | SKIP | 人手未実施 |
| TC-UX-02 | SKIP | 人手未実施 |
| TC-UX-03 | SKIP | 人手未実施 |
| TC-UX-04 | SKIP | 人手未実施 |
| TC-UX-05 | SKIP | 人手未実施 |
| TC-UX-06 | SKIP | 人手未実施 |
| TC-UX-07 | SKIP | 人手未実施 |
| TC-UX-08 | SKIP | 人手未実施 |
| TC-UX-09 | SKIP | 人手未実施 |
| TC-UX-10 | SKIP | 人手未実施 |
| TC-UX-11 | SKIP | 人手未実施 |
| TC-UX-12 | SKIP | 人手未実施 |

※ 人手 12 本は [UXシナリオ_内容同期.md](../UXシナリオ_内容同期.md) に従い実施し本表を更新する。
"@
Set-Content -Path (Join-Path $EvidenceRoot 'ux-checklist.md') -Value $checklist -Encoding UTF8

Set-Content -Path (Join-Path $PSScriptRoot '_latest_ux_sync_evidence.txt') -Value $EvidenceRoot -Encoding UTF8

if ($overallFail -eq 0) {
  Write-R "UX_SYNC_HARNESS_PASS"
  exit 0
}

Write-R "UX_SYNC_HARNESS_FAIL failures=$overallFail"
exit 1
