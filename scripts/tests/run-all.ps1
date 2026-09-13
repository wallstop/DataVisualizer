Set-StrictMode -Version 2.0

$tests = Get-ChildItem -Path $PSScriptRoot -Filter 'test-*.ps1' | Sort-Object -Property Name
if ($tests.Count -eq 0) {
    Write-Host 'No self-tests found.'
    exit 0
}

$failed = 0

if ($PSVersionTable.PSVersion.Major -ge 7) {
    # The test files use unique per-call temp roots and mutate nothing outside
    # them, so the same suites can run concurrently. Each file runs in a child
    # pwsh process with redirected streams: parallel-runspace host writes bypass
    # pipeline redirection, and the nested scripts the tests invoke must all be
    # captured. Output is replayed in name order to keep logs deterministic.
    $pwshExecutable = (Get-Process -Id $PID).Path
    $results = $tests | ForEach-Object -Parallel {
        $output = (& $using:pwshExecutable -NoProfile -File $_.FullName *>&1 | Out-String).TrimEnd()
        [pscustomobject]@{
            Name = $_.Name
            ExitCode = $LASTEXITCODE
            Output = $output
        }
    } -ThrottleLimit 4 | Sort-Object -Property Name
    foreach ($result in $results) {
        Write-Host ''
        Write-Host "Running $($result.Name)..."
        Write-Host $result.Output
        if ($result.ExitCode -ne 0) {
            $failed++
        }
    }
} else {
    foreach ($test in $tests) {
        Write-Host ''
        Write-Host "Running $($test.Name)..."
        & $test.FullName
        if ($LASTEXITCODE -ne 0) {
            $failed++
        }
    }
}

Write-Host ''
if ($failed -gt 0) {
    Write-Host "Self-tests: $failed of $($tests.Count) test file(s) failed."
    exit 1
}
Write-Host "Self-tests: all $($tests.Count) test file(s) passed."
exit 0