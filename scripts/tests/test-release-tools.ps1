Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$python = Get-Command python3 -ErrorAction SilentlyContinue
if ($null -eq $python) {
    $python = Get-Command python -ErrorAction SilentlyContinue
}
if ($null -eq $python) {
    Write-Host 'Python 3 is required for release tool tests.'
    exit 1
}

& $python.Source (Join-Path $repoRoot 'scripts/release/test_release_tools.py')
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
exit 0
