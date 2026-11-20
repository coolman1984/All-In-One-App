# Builds a share-ready copy of All in One in dist\ :
#   dist\AllInOne.exe              single exe, runs on Windows 10 / 11 with nothing to install
#   dist\All-in-One-portable.zip   the exe plus READ ME FIRST.txt, for sending to other people
# Does not touch the running app or your data. Usage: powershell -ExecutionPolicy Bypass -File tools\make-dist.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dist = Join-Path $root 'dist'
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$ico  = Join-Path $root 'src\app.ico'
if (-not (Test-Path $ico)) { throw 'src\app.ico is missing: run build.ps1 once first.' }
$src  = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:System.IO.Compression.dll'
$refs += '/resource:' + (Join-Path $root 'src\ChatSlides.pptx') + ',ChatSlides.pptx'
$wv = Join-Path $root 'lib\webview2'
$refs += ('/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.Core.dll')), ('/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll'))
foreach ($n in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') { $refs += '/resource:' + (Join-Path $wv $n) + ',webview2.' + $n }
$refs += '/resource:' + (Join-Path (Split-Path $wv) 'markdown\marked.umd.js') + ',md.marked.js'

New-Item -ItemType Directory -Path $dist -Force | Out-Null
$stage = Join-Path $dist 'All in One'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$exe = Join-Path $stage 'AllInOne.exe'
$readme = Join-Path $stage 'READ ME FIRST.txt'

# Bundle ffmpeg.exe.gz when available (same cache/shim resolution as build.ps1) so screen recording works out of the box.
$res = @()
$ff = Join-Path $root 'tools\bundle\ffmpeg.exe'
if (-not (Test-Path $ff)) { $c = Get-Command ffmpeg -ErrorAction SilentlyContinue; if ($c) { $ff = $c.Source } }
if ($ff -and (Test-Path $ff)) {
    $shim = [IO.Path]::ChangeExtension($ff, '.shim')
    if (Test-Path $shim) {
        $m = Select-String -Path $shim -Pattern '^\s*path\s*=\s*"?([^"\r\n]+)"?' | Select-Object -First 1
        if ($m -and (Test-Path $m.Matches[0].Groups[1].Value)) { $ff = $m.Matches[0].Groups[1].Value }
    }
}
if ($ff -and (Test-Path $ff)) {
    $cache = Join-Path $root 'build-cache'; New-Item -ItemType Directory -Force $cache | Out-Null
    $gz = Join-Path $cache 'ffmpeg.exe.gz'
    if (-not (Test-Path $gz) -or (Get-Item $gz).Length -lt 1MB -or (Get-Item $gz).LastWriteTimeUtc -lt (Get-Item $ff).LastWriteTimeUtc) {
        Write-Host "Packing ffmpeg into the distribution exe (first time only)..."
        $tmpGz = $gz + '.tmp'
        $in = [IO.File]::OpenRead($ff); $out = [IO.File]::Create($tmpGz)
        try {
            $z = New-Object IO.Compression.GZipStream($out, [IO.Compression.CompressionLevel]::Optimal)
            try { $in.CopyTo($z) } finally { $z.Dispose() }
        } finally { $out.Dispose(); $in.Dispose() }
        Move-Item -LiteralPath $tmpGz -Destination $gz -Force
    }
    $res += "/resource:$gz,tools.ffmpeg.exe.gz"
}

# Only framework assemblies that ship with Windows are referenced, so the exe has no other dependencies.
& $csc /nologo /target:winexe /optimize+ /platform:x64 /out:$exe /win32icon:$ico $refs $res $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

@'
All in One
==========

What you need
- Windows 10 or Windows 11 (64-bit). Nothing else: the app uses .NET Framework 4.8, which is part of Windows.
- No administrator rights.

Start
1. Unzip this folder anywhere (for example Documents or the Desktop). Do not run it from inside the zip.
2. Double-click AllInOne.exe.
3. On the first start the app offers to install itself for your Windows account:
   - Yes: it is copied to %LocalAppData%\Programs\All in One, added to the Start menu and the Desktop,
     starts with Windows, and can be removed in Settings > Apps > All in One.
   - No: it runs from this folder (portable). Your data is kept in the "data" folder next to the exe.

If Windows shows "Windows protected your PC"
- The exe is not signed with a paid certificate. Click "More info", then "Run anyway".
- If the file was blocked after download: right-click AllInOne.exe > Properties > tick "Unblock" > OK.

Where your data is
- Installed: %LocalAppData%\AllInOne\data
- Portable: the "data" folder next to AllInOne.exe (or %LocalAppData%\AllInOne\data if that folder is read-only)
- Settings > Your data shows the folder and can move it.

Optional
- Coding agents (Agents page) use the Claude Code, Codex, Cline, Antigravity or OpenCode command-line tools
  when they are installed. Everything else works without them.
- AllInOne.exe --data <folder>   use a folder of your choice for one run
- AllInOne.exe --uninstall       remove the installed copy (same as Settings > Apps)
'@ | Set-Content -LiteralPath $readme -Encoding utf8

$zip = Join-Path $dist 'All-in-One-portable.zip'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryZip = Join-Path $dist ('portable-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    $archive = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in @($exe, $readme)) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file, ('All in One/' + [IO.Path]::GetFileName($file)))
        }
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
} finally { if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force } }
Copy-Item -LiteralPath $exe -Destination (Join-Path $dist 'AllInOne.exe') -Force
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
"SHA256  $hash  AllInOne.exe" | Set-Content (Join-Path $dist 'SHA256.txt') -Encoding ascii
Write-Host "Built $zip"
Write-Host ("exe {0:N0} KB  zip {1:N0} KB" -f ((Get-Item $exe).Length / 1KB), ((Get-Item $zip).Length / 1KB))
