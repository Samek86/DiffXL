#Requires -Version 5.1
<#
.SYNOPSIS
  DiffXL content-scroll three-stage harness (T1/T2 PerfectSmoke + T3 auto-live).

.DESCRIPTION
  1. msbuild DiffXL Debug|x64
  2. Compile & run ContentScrollPerfectSmoke (COM-free, expected.json)
  3. Run DiffXL.exe --auto-live-test with content_scroll samples
  4. Copy reports into エビデンス_content_scroll_yyyyMMdd_HHmmss/
#>
param(
  [switch]$SkipBuild,
  [switch]$SkipLive,
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
$SmokeSrc = Join-Path $SrcRoot '_smoke\ContentScrollPerfectSmoke.cs'
$Samples = Join-Path $RepoRoot '30_参考資料\samples'
$Left = Join-Path $Samples 'content_scroll_left.xlsx'
$Right = Join-Path $Samples 'content_scroll_right.xlsx'
$Expected = Join-Path $Samples 'content_scroll_expected.json'
$LatestReport = Join-Path $PSScriptRoot '_latest_content_scroll_perfect.txt'
$Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $EvidenceRoot = Join-Path $PSScriptRoot ("エビデンス_content_scroll_{0}" -f $stamp)
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

$overallFail = 0
Write-R "BEGIN content-scroll harness RepoRoot=$RepoRoot"
Write-R "Evidence=$EvidenceRoot"

# ---- samples ----
foreach ($p in @($Left, $Right, $Expected)) {
  if (-not (Test-Path $p)) {
    Write-R "FAIL missing sample: $p"
    $overallFail++
  }
}
if ($overallFail -gt 0) {
  Write-R "ABORT missing samples"
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

# ---- 2. ContentScrollPerfectSmoke (T1/T2) ----
if (-not (Test-Path $Csc)) {
  Write-R "FAIL csc not found: $Csc"
  exit 6
}
$smokeOut = Join-Path $BinDir 'ContentScrollPerfectSmoke.exe'
Write-R "csc PerfectSmoke -> $smokeOut"
& $Csc /nologo /platform:x64 /optimize+ /r:"$BinDir\DiffXL.exe" /out:$smokeOut $SmokeSrc
if ($LASTEXITCODE -ne 0) {
  Write-R "FAIL csc ContentScrollPerfectSmoke exit=$LASTEXITCODE"
  exit 7
}
Copy-Item -Force $smokeOut (Join-Path $SrcRoot '_smoke\ContentScrollPerfectSmoke.exe') -ErrorAction SilentlyContinue
Copy-Item -Force $SmokeSrc (Join-Path $SrcRoot '_smoke\ContentScrollPerfectSmoke.cs') -ErrorAction SilentlyContinue

$smokeLog = Join-Path $EvidenceRoot 'perfect-smoke.txt'
Write-R "RUN PerfectSmoke"
Push-Location $BinDir
try {
  $smokeOutText = & .\ContentScrollPerfectSmoke.exe --left $Left --right $Right --expected $Expected 2>&1 | Out-String
  $smokeExit = $LASTEXITCODE
} finally {
  Pop-Location
}
Set-Content -Path $smokeLog -Value $smokeOutText -Encoding UTF8
Write-Host $smokeOutText
Write-R "PerfectSmoke exit=$smokeExit"
if ($smokeExit -ne 0 -or $smokeOutText -notmatch 'PERFECT_SCROLL_PASS') {
  Write-R "FAIL T1/T2 PerfectSmoke"
  $overallFail++
} else {
  Write-R "PASS T1/T2 PERFECT_SCROLL_PASS"
}

# ---- 3. Auto-live (T3) ----
if (-not $SkipLive) {
  if (Test-Path $LatestReport) { Remove-Item -Force $LatestReport -ErrorAction SilentlyContinue }
  Get-Process -Name DiffXL -ErrorAction SilentlyContinue | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch {}
  }
  Start-Sleep -Seconds 1

  Write-R "RUN auto-live timeout=${LiveTimeoutSec}s"
  $argList = @(
    '--auto-live-test',
    '--left', $Left,
    '--right', $Right,
    '--report', $LatestReport
  )
  $proc = Start-Process -FilePath $exe -ArgumentList $argList -PassThru -WindowStyle Minimized
  $exited = $proc.WaitForExit($LiveTimeoutSec * 1000)
  if (-not $exited) {
    Write-R "TIMEOUT auto-live after ${LiveTimeoutSec}s"
    try { Stop-Process -Id $proc.Id -Force } catch {}
    $overallFail++
  } else {
    Write-R "auto-live exit=$($proc.ExitCode)"
    if ($proc.ExitCode -ne 0) { $overallFail++ }
  }

  if (Test-Path $LatestReport) {
    Copy-Item -Force $LatestReport (Join-Path $EvidenceRoot 'auto-live-report.txt')
    $reportText = Get-Content -Path $LatestReport -Raw -ErrorAction SilentlyContinue
    if ($reportText -match 'AUTO_LIVE_PASS' -and $reportText -match 'FAILURES=0') {
      Write-R "PASS T3 AUTO_LIVE_PASS"
    } else {
      Write-R "FAIL T3 auto-live (see auto-live-report.txt)"
      $overallFail++
    }
  } else {
    Write-R "FAIL T3 report missing: $LatestReport"
    $overallFail++
  }
} else {
  Write-R "LIVE skipped"
}

# ---- 4. Evidence README ----
$readme = @"
# content_scroll evidence

- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
- Repo: $RepoRoot
- Left: $Left
- Right: $Right
- Expected: $Expected
- OverallFail: $overallFail

## Stages
- T1/T2: perfect-smoke.txt (PERFECT_SCROLL_PASS required)
- T3: auto-live-report.txt (AUTO_LIVE_PASS; Excel COM)

## Re-run
``````powershell
powershell -File "10_管理資料\テスト\run-content-scroll-test.ps1"
``````
"@
Set-Content -Path (Join-Path $EvidenceRoot 'README.md') -Value $readme -Encoding UTF8

# also keep a short pointer
Set-Content -Path (Join-Path $PSScriptRoot '_latest_content_scroll_evidence.txt') -Value $EvidenceRoot -Encoding UTF8

if ($overallFail -eq 0) {
  Write-R "CONTENT_SCROLL_HARNESS_PASS"
  exit 0
}

Write-R "CONTENT_SCROLL_HARNESS_FAIL failures=$overallFail"
exit 1
