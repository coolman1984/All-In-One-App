$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$capture = $assembly.GetType('StickyPro.CaptureCommands')
$desktop = New-Object Drawing.Rectangle(-1920, -200, 3840, 1280)
$point = New-Object Drawing.Point(-1900, -190)
$size = New-Object Drawing.Size(800, 600)
$r = $capture.GetMethod('FixedAt', $static).Invoke($null, @($point.PSObject.BaseObject, $size.PSObject.BaseObject, $desktop.PSObject.BaseObject))
if (-not $desktop.Contains($r) -or $r.Width -ne 800 -or $r.Height -ne 600) { throw 'Fixed region did not fit negative-coordinate desktop.' }
foreach ($pair in @(@('PointNative',8),@('RectNative',16))) {
    $value = [Activator]::CreateInstance($assembly.GetType('StickyPro.CaptureNative+' + $pair[0]))
    if ([Runtime.InteropServices.Marshal]::SizeOf($value) -ne $pair[1]) { throw 'Capture native ABI mismatch.' }
}
$modes = $capture.GetField('Modes', $static).GetValue($null)
foreach ($mode in @('rect','window','full','fixed','control','free')) { if (@($modes | Where-Object { $_[0] -eq $mode }).Count -ne 1) { throw ('Capture mode missing: ' + $mode) } }
Write-Host 'CAPTURE OK: fixed-region bounds including negative monitors; native layouts; mode catalog'
