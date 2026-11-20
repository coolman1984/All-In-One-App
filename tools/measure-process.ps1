# Read-only sampler. Run against the updated app after opening/closing the tools you use.
param([int]$ProcessId = 0, [ValidateRange(1,60)][int]$Seconds = 10)
$ErrorActionPreference = 'Stop'
if (-not $ProcessId) {
    $apps = @(Get-Process AllInOne -ErrorAction Stop)
    if ($apps.Count -ne 1) { throw 'Specify -ProcessId when multiple copies are running.' }
    $ProcessId = $apps[0].Id
    foreach ($app in $apps) { $app.Dispose() }
}
$process = [Diagnostics.Process]::GetProcessById($ProcessId)
try {
    $startCpu = $process.TotalProcessorTime.TotalMilliseconds
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $Seconds
    $process.Refresh()
    [pscustomobject]@{
        ProcessId=$ProcessId
        Seconds=[math]::Round($watch.Elapsed.TotalSeconds,2)
        MachineCpuPercent=[math]::Round(($process.TotalProcessorTime.TotalMilliseconds-$startCpu)/$watch.Elapsed.TotalMilliseconds/[Environment]::ProcessorCount*100,3)
        PrivateMB=[math]::Round($process.PrivateMemorySize64/1MB,2)
        WorkingSetMB=[math]::Round($process.WorkingSet64/1MB,2)
        Handles=$process.HandleCount
        Threads=$process.Threads.Count
    } | ConvertTo-Json
} finally { $process.Dispose() }
