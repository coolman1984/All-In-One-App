# Builds StickyNotesPro.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# No installs needed. Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$exe  = Join-Path $here 'StickyNotesPro.exe'
$ico  = Join-Path $here 'src\app.ico'
$src  = Get-ChildItem (Join-Path $here 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll'

# Ask a running copy to save and exit so the exe can be replaced.
$running = Get-Process StickyNotesPro -ErrorAction SilentlyContinue
if ($running) {
    Start-Process $exe -ArgumentList '--exit' -Wait
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

if (-not (Test-Path $ico)) {
    $tmp = Join-Path $here 'icon_tool.tmp.exe'
    & $csc /nologo /target:winexe /out:$tmp $refs $src
    if ($LASTEXITCODE -ne 0) { throw 'Build failed (icon step).' }
    Start-Process $tmp -ArgumentList '--make-icon',"`"$ico`"" -Wait
    Remove-Item $tmp -Force
}

& $csc /nologo /target:winexe /optimize+ /platform:x64 /out:$exe /win32icon:$ico $refs $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "Built $exe"
