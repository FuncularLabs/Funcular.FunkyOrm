$ErrorActionPreference = "Stop"

Write-Host "Running Performance and Entity Framework comparison tests..." -ForegroundColor Cyan

# Run tests with PERFORMANCE_TESTS defined, filtering for Performance or EntityFramework tests
dotnet test --filter "FullyQualifiedName~Performance|FullyQualifiedName~EntityFramework" /p:DefineConstants="PERFORMANCE_TESTS"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Performance tests completed successfully." -ForegroundColor Green
} else {
    Write-Host "Performance tests failed." -ForegroundColor Red
    exit $LASTEXITCODE
}
