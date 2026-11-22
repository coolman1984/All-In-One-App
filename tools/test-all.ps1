# Build and run all isolated checks with the runtime used by the desktop app.
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $runtime -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test-build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$failed = @()
foreach ($check in @('regressions','reliability','forms','dashboard','layout','office-edit','document-close','capture','snip-output','recorder','visual','responsiveness','explorer-paint','agent-conversations','leader-team','saved-work','assistant-harness','office-com','sqlite-vault','hermes-memory','workflow-recorder','scheduled-jobs')) {
    & $runtime -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot ('test-' + $check + '.ps1'))
    if ($LASTEXITCODE -ne 0) { $failed += $check }
}
if ($failed.Count) { throw ('Checks failed: ' + ($failed -join ', ')) }
Write-Host 'ALL CHECKS PASSED'
