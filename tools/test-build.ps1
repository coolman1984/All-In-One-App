# Compile check for reviewers: builds to build-test\ only. Never touches the running app or data\.
# -Name lets parallel checks write different files (for example -Name agent1.exe).
param([string]$Name = 'StickyNotesPro.test.exe', [switch]$OfflineUi)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out  = Join-Path $root 'build-test'
New-Item -ItemType Directory -Force $out | Out-Null
$src  = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:System.IO.Compression.dll'
$refs += '/resource:' + (Join-Path $root 'src\ChatSlides.pptx') + ',ChatSlides.pptx'
# Browser page: WebView2 SDK (lib\webview2), referenced and packed into the exe (unpacked on first use).
$wv = Join-Path $root 'lib\webview2'
$refs += ('/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.Core.dll')), ('/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll'))
foreach ($n in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') {
    if (-not $OfflineUi) { $refs += '/resource:' + (Join-Path $wv $n) + ',webview2.' + $n }
}
$refs += '/resource:' + (Join-Path (Split-Path $wv) 'markdown\marked.umd.js') + ',md.marked.js'
$outArg = '/out:' + (Join-Path $out $Name)
$icoArg = '/win32icon:' + (Join-Path $root 'src\app.ico')
& $csc /nologo /target:winexe /optimize+ /platform:x64 $outArg $icoArg $refs $src
if ($LASTEXITCODE -ne 0) { Write-Error 'BUILD FAILED'; exit 1 }
Write-Host 'BUILD OK'
