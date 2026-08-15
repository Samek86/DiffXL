#Requires -Version 5.1
<#
.SYNOPSIS
  Build DiffXL (Debug|x64) and run logic smokes. Exit 0 = all PASS.
#>
$ErrorActionPreference = 'Continue'

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$projRel = Join-Path $root '20_ソース\DiffXL\DiffXL\DiffXL.csproj'
if (-not (Test-Path $projRel)) {
    throw "DiffXL.csproj not found from script root: $PSScriptRoot"
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "vswhere not found: $vswhere"
}
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) {
    throw "MSBuild not found via vswhere"
}

$proj = "$root\20_ソース\DiffXL\DiffXL\DiffXL.csproj"
$bin = "$root\20_ソース\DiffXL\DiffXL\bin\x64\Debug"
$exe = "$bin\DiffXL.exe"
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$smoke = "$root\20_ソース\DiffXL\_smoke"

if (-not (Test-Path $csc)) {
    throw "csc not found: $csc"
}

function Build-DiffXL { & $msbuild $proj /restore /p:Configuration=Debug /p:Platform=x64 /v:m }
function Invoke-Smoke([string]$name) {
  & $csc /nologo /target:exe /platform:x64 /r:$exe /r:System.Drawing.dll /out:"$bin\$name.exe" "$smoke\$name.cs"
  if ($LASTEXITCODE -ne 0) { throw "csc $name failed" }
  Push-Location $bin
  try { & ".\$name.exe"; if ($LASTEXITCODE -ne 0) { throw "$name failed" } }
  finally { Pop-Location }
}

$names = @(
    'ContentDiffSmoke',
    'ContentStreamSmoke',
    'MiniMapViewportBandSmoke',
    'ImageOverlayAlignSmoke',
    'ImageSequenceSmoke',
    'SheetMatcherSmoke',
    'StreamPairLinkSmoke',
    'TableTruthSmoke',
    'TableRowDiffSmoke',
    'TableDetectorSmoke',
    'DiffPairIndexSmoke',
    'SheetLazyCompareSmoke'
)

Write-Host "Root=$root"
Write-Host "MSBuild=$msbuild"

Build-DiffXL
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL Build-DiffXL exit=$LASTEXITCODE"
    exit 1
}

$fail = 0
foreach ($name in $names) {
    $src = Join-Path $smoke "$name.cs"
    if (-not (Test-Path $src)) {
        Write-Host "FAIL $name (missing $src)"
        $fail++
        continue
    }

    Write-Host "==== $name ===="
    try {
        Invoke-Smoke $name
        Write-Host "PASS $name"
    } catch {
        Write-Host "FAIL $name : $_"
        $fail++
    }
}

if ($fail -eq 0) {
    Write-Host "ALL PASS"
} else {
    Write-Host "FAILED $fail"
}
exit $fail
