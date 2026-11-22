# Builds "All in One" (AllInOne.exe) with the C# compiler that ships with Windows (.NET Framework 4.x).
# No installs needed. Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
# Also creates the one-click launcher "All in One.lnk" in this folder.
# -Out <path>: release build for Team > Build & Publish: writes only that exe (no app restart, no shortcut).
param([string]$Out = '')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$exe  = if ($Out) { $Out } else { Join-Path $here 'AllInOne.exe' }
$old  = Join-Path $here 'StickyNotesPro.exe'
$ico  = Join-Path $here 'src\app.ico'
$src  = Get-ChildItem (Join-Path $here 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:System.IO.Compression.dll'
$refs += '/resource:' + (Join-Path $here 'src\ChatSlides.pptx') + ',ChatSlides.pptx'
# Browser page: WebView2 SDK (lib\webview2), referenced and packed into the exe (unpacked on first use).
$wv = Join-Path $here 'lib\webview2'
$refs += ('/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.Core.dll')), ('/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll'))
foreach ($n in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') { $refs += '/resource:' + (Join-Path $wv $n) + ',webview2.' + $n }
$refs += '/resource:' + (Join-Path (Split-Path $wv) 'markdown\marked.umd.js') + ',md.marked.js'

. (Join-Path $here 'tools\bundle-ffmpeg.ps1')
$res = "/resource:$(Get-BundledFfmpeg $here),tools.ffmpeg.exe.gz"

# Ask a running copy (new or old name) to save and exit so the exe can be replaced.
if (-not $Out) { foreach ($name in 'AllInOne', 'StickyNotesPro') {
    $path = if ($name -eq 'AllInOne') { $exe } else { $old }
    $running = Get-Process $name -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $path }
    if ($running) {
        Start-Process $path -ArgumentList '--exit' -Wait
        $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
        if (Get-Process $name -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $path }) {
            throw 'The running app could not exit. Save your work and close it before rebuilding.'
        }
    }
} }

# Always regenerate the icon from the logo drawn in code, so exe, tray and windows match.
$tmp = Join-Path $here 'icon_tool.tmp.exe'
& $csc /nologo /target:winexe /out:$tmp $refs $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed (icon step).' }
try {
    $iconProcess = Start-Process $tmp -ArgumentList '--make-icon',"`"$ico`"" -Wait -PassThru
    if ($iconProcess.ExitCode -ne 0) { throw 'Icon generation failed.' }
} finally { Remove-Item -LiteralPath $tmp -Force }

& $csc /nologo /target:winexe /optimize+ /platform:x64 /out:$exe /win32icon:$ico $refs $res $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Out) { Write-Host "Built $exe"; exit 0 }
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
