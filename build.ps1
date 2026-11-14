# Builds "All in One" (AllInOne.exe) with the C# compiler that ships with Windows (.NET Framework 4.x).
# No installs needed. Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
# Also creates the one-click launcher "All in One.lnk" in this folder.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$exe  = Join-Path $here 'AllInOne.exe'
$old  = Join-Path $here 'StickyNotesPro.exe'
$ico  = Join-Path $here 'src\app.ico'
$src  = Get-ChildItem (Join-Path $here 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll'

# Ask a running copy (new or old name) to save and exit so the exe can be replaced.
foreach ($name in 'AllInOne', 'StickyNotesPro') {
    $running = Get-Process $name -ErrorAction SilentlyContinue
    if ($running) {
        $path = if ($name -eq 'AllInOne') { $exe } else { $old }
        if (Test-Path $path) { Start-Process $path -ArgumentList '--exit' -Wait }
        $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }
}

# Always regenerate the icon from the logo drawn in code, so exe, tray and windows match.
$tmp = Join-Path $here 'icon_tool.tmp.exe'
& $csc /nologo /target:winexe /out:$tmp $refs $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed (icon step).' }
Start-Process $tmp -ArgumentList '--make-icon',"`"$ico`"" -Wait
Remove-Item $tmp -Force

& $csc /nologo /target:winexe /optimize+ /platform:x64 /out:$exe /win32icon:$ico $refs $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if (Test-Path $old) { Remove-Item $old -Force -ErrorAction SilentlyContinue }

# One-click launcher in the project root.
$lnk = Join-Path $here 'All in One.lnk'
$shell = New-Object -ComObject WScript.Shell
$s = $shell.CreateShortcut($lnk)
$s.TargetPath = $exe
$s.WorkingDirectory = $here
$s.IconLocation = "$exe,0"
$s.Description = 'All in One - notes, prompts, clipboard, capture and system tools'
$s.Save()
Write-Host "Built $exe"
