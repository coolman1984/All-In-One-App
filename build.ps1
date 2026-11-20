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
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:System.IO.Compression.dll'
$refs += '/resource:' + (Join-Path $here 'src\ChatSlides.pptx') + ',ChatSlides.pptx'
# Browser page: WebView2 SDK (lib\webview2), referenced and packed into the exe (unpacked on first use).
$wv = Join-Path $here 'lib\webview2'
$refs += ('/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.Core.dll')), ('/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll'))
foreach ($n in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') { $refs += '/resource:' + (Join-Path $wv $n) + ',webview2.' + $n }
$refs += '/resource:' + (Join-Path (Split-Path $wv) 'markdown\marked.umd.js') + ',md.marked.js'

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

# Tools packed inside the exe for PCs that do not have them (unpacked on first use to %LocalAppData%\AllInOne\tools):
# ffmpeg for screen recording. Source: tools\bundle\ffmpeg.exe, else the ffmpeg on this PC. Compressed once, then cached.
$res = @()
$ff = Join-Path $here 'tools\bundle\ffmpeg.exe'
if (-not (Test-Path $ff)) { $c = Get-Command ffmpeg -ErrorAction SilentlyContinue; if ($c) { $ff = $c.Source } }
if ($ff -and (Test-Path $ff)) {
    # Dereference Scoop .shim stubs (e.g. scoop\shims\ffmpeg.exe -> scoop\apps\ffmpeg\current\bin\ffmpeg.exe).
    $shim = [IO.Path]::ChangeExtension($ff, '.shim')
    if (Test-Path $shim) {
        $m = Select-String -Path $shim -Pattern '^\s*path\s*=\s*"?([^"\r\n]+)"?' | Select-Object -First 1
        if ($m -and (Test-Path $m.Matches[0].Groups[1].Value)) { $ff = $m.Matches[0].Groups[1].Value }
    }
}
if (Test-Path $ff) {
    $cache = Join-Path $here 'build-cache'; New-Item -ItemType Directory -Force $cache | Out-Null
    $gz = Join-Path $cache 'ffmpeg.exe.gz'
    if (-not (Test-Path $gz) -or (Get-Item $gz).Length -lt 1MB -or (Get-Item $gz).LastWriteTimeUtc -lt (Get-Item $ff).LastWriteTimeUtc) {
        Write-Host "Packing ffmpeg into the exe (first time only)..."
        $tmpGz = $gz + '.tmp'
        $in = [IO.File]::OpenRead($ff); $out = [IO.File]::Create($tmpGz)
        try {
            $z = New-Object IO.Compression.GZipStream($out, [IO.Compression.CompressionLevel]::Optimal)
            try { $in.CopyTo($z) } finally { $z.Dispose() }
        } finally { $out.Dispose(); $in.Dispose() }
        Move-Item -LiteralPath $tmpGz -Destination $gz -Force
    }
    $res += "/resource:$gz,tools.ffmpeg.exe.gz"
} else { Write-Host "ffmpeg not found: building without it (screen recording then needs ffmpeg on the PC)." }

& $csc /nologo /target:winexe /optimize+ /platform:x64 /out:$exe /win32icon:$ico $refs $res $src
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
