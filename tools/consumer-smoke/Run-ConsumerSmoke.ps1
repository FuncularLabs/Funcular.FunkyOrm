<#
.SYNOPSIS
  Consumer smoke check for the Funcular.Data.Orm package (docs/plans/DECISIONS-3.10.2.md FD-05, backlog WP-E1).

.DESCRIPTION
  Packs Funcular.Data.Orm.SqlServer into a local feed under %TEMP%, copies the ConsumerSmoke console app next to it,
  restores the app against that feed plus nuget.org (so the drivers the package declares resolve at their declared
  minimums, never higher), and runs it for net8.0 (every provider) and net48 (SQL Server only). The app fails when a
  driver assembly is missing or isn't the expected minimum. Nothing is published anywhere.

  Also fails if any driver PackageReference in the five library csprojs floats (contains '*').

.PARAMETER Version
  The package version to pack and install (the csprojs' <Version>). Default 3.12.0.
.PARAMETER Expected
  name=version pairs the app checks: the informational version of each driver assembly a consumer gets.
.PARAMETER SkipNet48
  Don't run the net48 target.
.PARAMETER SkipFloatCheck
  Diagnostic: run the consumer even when a csproj floats a driver version (to see what a consumer then gets).
.PARAMETER Keep
  Keep the work directory under %TEMP% (printed at the end).

.EXAMPLE
  pwsh -File tools/consumer-smoke/Run-ConsumerSmoke.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = "3.12.0",
    [string]$Configuration = "Release",
    [string[]]$Expected = @("Microsoft.Data.SqlClient=6.0.0", "Npgsql=9.0.5", "MySqlConnector=2.6.2", "Microsoft.Data.Sqlite=8.0.32"),
    [switch]$SkipNet48,
    [switch]$SkipFloatCheck,
    [switch]$Keep
)

$ErrorActionPreference = "Stop"
# Forward slashes: Join-Path on Linux/macOS would keep a backslash as part of the file name (review R-1, B-8).
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$projects = @(
    "Funcular.Data.Orm.Core/Funcular.Data.Orm.Core.csproj",
    "Funcular.Data.Orm.SqlServer/Funcular.Data.Orm.SqlServer.csproj",
    "Funcular.Data.Orm.PostgreSql/Funcular.Data.Orm.PostgreSql.csproj",
    "Funcular.Data.Orm.MySql/Funcular.Data.Orm.MySql.csproj",
    "Funcular.Data.Orm.Sqlite/Funcular.Data.Orm.Sqlite.csproj")
# net48 runs on Windows only (the .NET Framework runtime); Windows PowerShell 5.1 has no $IsWindows, so ask the runtime.
$onWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
if (-not $onWindows -and -not $SkipNet48) { Write-Host "Not on Windows: skipping the net48 target."; $SkipNet48 = $true }

$floating = @()
foreach ($project in $projects) {
    $xml = [xml](Get-Content (Join-Path $repo $project) -Raw)
    foreach ($reference in $xml.SelectNodes("//PackageReference")) {
        $v = $reference.GetAttribute("Version")
        if ([string]::IsNullOrEmpty($v) -or $v.Contains("*")) { $floating += "$project : $($reference.GetAttribute('Include')) Version='$v'" }
    }
}
if ($floating.Count -gt 0) {
    Write-Host "FAIL: floating package references:" -ForegroundColor Red
    $floating | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    if (-not $SkipFloatCheck) { exit 2 }
    Write-Host "(continuing: -SkipFloatCheck)" -ForegroundColor Yellow
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("funky-smoke-" + [Guid]::NewGuid().ToString("N"))
$feed = Join-Path $work "feed"
$app = Join-Path $work "app"
New-Item -ItemType Directory -Path $feed, $app | Out-Null
Write-Host "Work directory: $work"

Write-Host "Packing Funcular.Data.Orm $Version ($Configuration) into the local feed..."
& dotnet pack (Join-Path $repo "Funcular.Data.Orm.SqlServer/Funcular.Data.Orm.SqlServer.csproj") -c $Configuration -o $feed --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: pack" -ForegroundColor Red; exit 3 }
$nupkg = Join-Path $feed "Funcular.Data.Orm.$Version.nupkg"
if (-not (Test-Path $nupkg)) { Write-Host "FAIL: $nupkg not produced (is <Version> $Version?)" -ForegroundColor Red; exit 3 }

Copy-Item (Join-Path $PSScriptRoot "ConsumerSmoke/*") $app -Recurse
$env:FUNKY_SMOKE_FEED = $feed
# A packages folder of its own: the machine's global cache may already hold a Funcular.Data.Orm of this version
# (an earlier pack, or the published one), and NuGet would use that instead of the package just packed.
$packages = Join-Path $work "packages"
$failed = $false
$frameworks = if ($SkipNet48) { @("net8.0") } else { @("net8.0", "net48") }
foreach ($framework in $frameworks) {
    Write-Host ""
    Write-Host "=== $framework ===" -ForegroundColor Cyan
    & dotnet run --project (Join-Path $app "ConsumerSmoke.csproj") -c $Configuration -f $framework -p:FunkyVersion=$Version -p:RestorePackagesPath=$packages --nologo -v q -- @Expected
    if ($LASTEXITCODE -ne 0) { $failed = $true; Write-Host "FAIL: $framework" -ForegroundColor Red }
    else { Write-Host "PASS: $framework" -ForegroundColor Green }
}

if (-not $Keep) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue } else { Write-Host "Kept: $work" }
if ($failed) { exit 1 }
Write-Host ""
Write-Host "PASS: the package installs alone and every provider loads its driver at the declared minimum." -ForegroundColor Green
exit 0
