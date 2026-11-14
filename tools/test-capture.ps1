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
$source = New-Object Drawing.Bitmap(64, 600)
for ($y=0; $y -lt 600; $y++) { for ($x=0; $x -lt 64; $x++) { $source.SetPixel($x,$y,[Drawing.Color]::FromArgb(($y*73+$x*7)%256,($y*37+$x*13)%256,($y*19+$x*3)%256)) } }
$format = [Drawing.Imaging.PixelFormat]::Format32bppArgb
$a = $source.Clone((New-Object Drawing.Rectangle(0,0,64,200)), $format)
$b = $source.Clone((New-Object Drawing.Rectangle(0,60,64,200)), $format)
$type = $assembly.GetType('StickyPro.ScrollStitcher')
$offset = $type.GetMethod('FindOffset', $static).Invoke($null, @($a.PSObject.BaseObject,$b.PSObject.BaseObject,60))
if ($offset -ne 60) { throw "Overlap matcher found $offset instead of 60." }
$stitch = [Activator]::CreateInstance($type, [Reflection.BindingFlags]'Public,NonPublic,Instance', $null, [object[]]@([long]1000000), $null)
try {
    [void]$stitch.Add($a); [void]$stitch.Add($b)
    $result = $stitch.Build()
    try { if ($result.Height -ne 260 -or $result.GetPixel(10,230).ToArgb() -ne $source.GetPixel(10,230).ToArgb()) { throw 'Stitched image has duplicate or missing rows.' } }
    finally { $result.Dispose() }
} finally { $stitch.Dispose(); $source.Dispose() }
Write-Host 'STITCH OK: synthetic overlap matched; appended bands reproduce the original pixels without duplicate rows'
