# Tests recorder shutdown using an owned, hidden sleeping PowerShell process.
# Never starts the app, ffmpeg, a capture, Store.Load, or clipboard operations.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$instance = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$recorder = $assembly.GetType('StickyPro.Recorder')
$appType = $assembly.GetType('StickyPro.App')
$context = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($appType)
$sync = New-Object Windows.Forms.Control
$unused = $sync.Handle
$appType.GetField('Sync', $instance).SetValue($context, $sync.PSObject.BaseObject)
$appType.GetField('I', $static).SetValue($null, $context)
$fixture = Join-Path $root ('build-test\recorder-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$assembly.GetType('StickyPro.Store').GetField('Dir', $static).SetValue($null, $fixture)
$area = New-Object Drawing.Rectangle(100, 100, 200, 200)
$bar = [Activator]::CreateInstance($assembly.GetType('StickyPro.RecordBar'), $instance, $null, [object[]]@($area.PSObject.BaseObject), $null)
$unused = $bar.Handle # hidden handle only; Show is never called
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$start.Arguments = '-NoProfile -NonInteractive -Command "Start-Sleep -Milliseconds 1800"'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = 'Hidden'
$start.RedirectStandardInput = $true
$owned = [Diagnostics.Process]::Start($start)
try {
    $recorder.GetField('bar', $static).SetValue($null, $bar.PSObject.BaseObject)
    $recorder.GetField('ff', $static).SetValue($null, $owned.PSObject.BaseObject)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    [void]$recorder.GetMethod('Stop', $static).Invoke($null, @($false))
    if ($timer.ElapsedMilliseconds -gt 750) { throw 'Recorder Stop blocked the UI caller.' }
    if (-not $recorder.GetField('Saving', $static).GetValue($null)) { throw 'Recorder did not track its pending background shutdown.' }
    while ($recorder.GetField('Saving', $static).GetValue($null) -and $timer.ElapsedMilliseconds -lt 12000) {
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 25
    }
    if ($recorder.GetField('Saving', $static).GetValue($null)) { throw 'Recorder never completed its background shutdown.' }
    if ($bar.Visible) { throw 'Recorder test displayed a window.' }
    # A stop requested during asynchronous pause must run once pause has finished.
    $bar = [Activator]::CreateInstance($assembly.GetType('StickyPro.RecordBar'), $instance, $null, [object[]]@($area.PSObject.BaseObject), $null)
    $unused = $bar.Handle
    $owned = [Diagnostics.Process]::Start($start)
    $recorder.GetField('bar', $static).SetValue($null, $bar.PSObject.BaseObject)
    $recorder.GetField('ff', $static).SetValue($null, $owned.PSObject.BaseObject)
    $recorder.GetField('Paused', $static).SetValue($null, $false)
    $timer.Restart()
    [void]$recorder.GetMethod('TogglePause', $static).Invoke($null, @())
    [void]$recorder.GetMethod('Stop', $static).Invoke($null, @($false))
    if ($timer.ElapsedMilliseconds -gt 750) { throw 'Pause/queued Stop blocked the UI caller.' }
    while (($recorder.GetProperty('Active', $static).GetValue($null, $null) -or $recorder.GetField('Saving', $static).GetValue($null)) -and $timer.ElapsedMilliseconds -lt 12000) {
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 25
    }
    if ($recorder.GetProperty('Active', $static).GetValue($null, $null) -or $recorder.GetField('Saving', $static).GetValue($null)) { throw 'Stop requested during pause was lost.' }
    Write-Host 'RECORDER OK: nonblocking stop and pause/queued-stop; owned helpers finish in background; UI completion delivered'
} finally {
    try { if (-not $owned.HasExited) { $owned.Kill() } } catch { }
    $owned.Dispose(); $bar.Dispose(); $sync.Dispose()
    $appType.GetField('I', $static).SetValue($null, $null)
}
