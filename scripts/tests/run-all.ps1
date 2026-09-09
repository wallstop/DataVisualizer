Set-StrictMode -Version 2.0

$tests = Get-ChildItem -Path $PSScriptRoot -Filter 'test-*.ps1' | Sort-Object -Property Name
if ($tests.Count -eq 0) {
    Write-Host 'No self-tests found.'
    exit 0
}

$failed = 0
foreach ($test in $tests) {
    Write-Host ''
    Write-Host "Running $($test.Name)..."
    & $test.FullName
    if ($LASTEXITCODE -ne 0) {
        $failed++
    }
}

Write-Host ''
if ($failed -gt 0) {
    Write-Host "Self-tests: $failed of $($tests.Count) test file(s) failed."
    exit 1
}
Write-Host "Self-tests: all $($tests.Count) test file(s) passed."
exit 0
