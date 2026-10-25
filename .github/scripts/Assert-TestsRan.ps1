<#
.SYNOPSIS
    Fails a CI test job when a test in its TRX didn't run and isn't on the job's allowlist, or when the TRX holds no
    results at all (3.12.0, docs/plans/NULL_SEMANTICS_PLAN.md §8, R3).

.DESCRIPTION
    MSTest records an Assert.Inconclusive (and an [Ignore]) as outcome="NotExecuted", and `dotnet test` still exits 0,
    so a suite whose database was missing reads as green. Each NotExecuted result is named by its test definition:
    TestMethod@className (the runtime class's full name, so an inherited harness test carries the derived class) and
    TestMethod@name (the method, without data-row arguments, whose format differs between MSTest versions).

.PARAMETER Trx
    The TRX file, or a wildcard; every match is read.

.PARAMETER Allow
    The tests allowed not to run, each "<class full name>.<method>". Several may be given as an array or as one
    comma-separated string.
#>
param(
    [Parameter(Mandatory = $true)][string] $Trx,
    [string[]] $Allow = @()
)

$ErrorActionPreference = 'Stop'
$allowed = @($Allow | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })

$files = @(Get-ChildItem -Path $Trx -File -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) {
    Write-Host "No TRX file matches '$Trx'."
    exit 1
}

$namespace = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
$total = 0
$unexpected = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    [xml] $document = Get-Content -LiteralPath $file.FullName -Raw
    $names = @{}
    foreach ($definition in @(Select-Xml -Xml $document -Namespace $namespace -XPath '//t:TestDefinitions/t:UnitTest')) {
        $method = $definition.Node.TestMethod
        $names[$definition.Node.id] = "$($method.className).$($method.name)"
    }
    foreach ($result in @(Select-Xml -Xml $document -Namespace $namespace -XPath '//t:Results/t:UnitTestResult')) {
        $total++
        if ($result.Node.outcome -ne 'NotExecuted') {
            continue
        }
        $name = $names[$result.Node.testId]
        if (-not $name) {
            $name = "<no definition for test $($result.Node.testId)>"
        }
        if ($allowed -notcontains $name) {
            $unexpected.Add("$name ($($result.Node.testName))")
        }
    }
}

if ($total -eq 0) {
    Write-Host "The TRX at '$Trx' holds no test results."
    exit 1
}
if ($unexpected.Count -gt 0) {
    Write-Host "These tests didn't run (inconclusive or ignored) and aren't on this job's allowlist:"
    $unexpected | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "$total results; every test that didn't run is on the allowlist."
exit 0
