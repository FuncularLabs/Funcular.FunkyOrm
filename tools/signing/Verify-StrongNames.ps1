<#
.SYNOPSIS
  Verifies that every assembly in a packed Funcular.Data.Orm nupkg carries a full strong-name signature with the
  Funcular Labs key (docs/plans/RELEASE_3.14.0_PLAN.md, stream N).

.DESCRIPTION
  Extracts each .nupkg under -NupkgDirectory, finds every DLL under lib/, and runs `sn.exe -vf` (the signature must
  verify: a public-signed or unsigned assembly fails) and `sn.exe -T` (the public-key token must be -ExpectedToken).
  sn.exe is located under the .NET Framework SDK (NETFX 4.8*Tools); the script fails if it is absent, a nupkg has no
  DLL, or any DLL fails either check. Exit 0 only when every DLL passes. Windows only (the publish job runs there).

.EXAMPLE
  pwsh -File tools/signing/Verify-StrongNames.ps1 -NupkgDirectory nupkgs -ExpectedToken 0650eba294fc9e3f
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$NupkgDirectory,
    [string]$ExpectedToken = "0650eba294fc9e3f"
)
$ErrorActionPreference = "Stop"

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} "Microsoft SDKs\Windows\v10.0A\bin"
$candidates = @()
if (Test-Path $sdkRoot) {
    $candidates = Get-ChildItem -Path $sdkRoot -Directory -Filter "NETFX 4.8*Tools" |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "sn.exe" } |
        Where-Object { Test-Path $_ }
}
if (-not $candidates) { Write-Host "FAIL: sn.exe not found under $sdkRoot (NETFX 4.8*Tools): the .NET Framework 4.8 SDK is required." -ForegroundColor Red; exit 2 }
$sn = $candidates | Select-Object -First 1
Write-Host "sn.exe: $sn"

$packages = @(Get-ChildItem -Path $NupkgDirectory -Filter *.nupkg -File)
if (-not $packages) { Write-Host "FAIL: no .nupkg under $NupkgDirectory" -ForegroundColor Red; exit 2 }

$failures = @()
$checked = 0
foreach ($package in $packages) {
    $work = Join-Path ([IO.Path]::GetTempPath()) ("funky-sn-" + [Guid]::NewGuid().ToString("N"))
    try {
        # Not Expand-Archive: Windows PowerShell 5.1's refuses the .nupkg extension (NotSupportedArchiveFileExtension).
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($package.FullName, $work)
        $dlls = @(Get-ChildItem -Path (Join-Path $work "lib") -Recurse -Filter *.dll -File -ErrorAction SilentlyContinue)
        if (-not $dlls) { $failures += "$($package.Name): no DLL under lib/"; continue }
        foreach ($dll in $dlls) {
            $checked++
            $relative = $dll.FullName.Substring($work.Length + 1)
            $verify = & $sn -q -vf $dll.FullName 2>&1
            if ($LASTEXITCODE -ne 0) { $failures += "$($package.Name): $relative is not fully signed: $verify"; continue }
            $tokenText = (& $sn -q -T $dll.FullName 2>&1 | Out-String)
            $token = ([regex]::Match($tokenText, "([0-9a-f]{16})", "IgnoreCase")).Groups[1].Value.ToLowerInvariant()
            if ($token -ne $ExpectedToken.ToLowerInvariant()) { $failures += "$($package.Name): $relative has token '$token', expected $ExpectedToken" }
            else { Write-Host "  ok  $($package.Name): $relative ($token)" }
        }
    }
    finally {
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
}

if ($failures.Count -gt 0) {
    Write-Host "FAIL: $($failures.Count) of $checked DLL checks failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "PASS: $checked DLLs across $($packages.Count) package(s) are fully strong-name signed with token $ExpectedToken." -ForegroundColor Green
exit 0
