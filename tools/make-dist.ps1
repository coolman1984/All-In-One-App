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
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll'

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null
$stage = Join-Path $dist 'All in One'
New-Item -ItemType Directory $stage | Out-Null
$exe = Join-Path $stage 'AllInOne.exe'

# Only framework assemblies that ship with Windows are referenced, so the exe has no other dependencies.
& $csc /nologo /target:winexe /optimize+ /platform:x64 /out:$exe /win32icon:$ico $refs $src
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
'@ | Set-Content (Join-Path $stage 'READ ME FIRST.txt') -Encoding utf8

$zip = Join-Path $dist 'All-in-One-portable.zip'
Compress-Archive -Path $stage -DestinationPath $zip
Copy-Item $exe (Join-Path $dist 'AllInOne.exe')
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
"SHA256  $hash  AllInOne.exe" | Set-Content (Join-Path $dist 'SHA256.txt') -Encoding ascii
Write-Host "Built $zip"
Write-Host ("exe {0:N0} KB  zip {1:N0} KB" -f ((Get-Item $exe).Length / 1KB), ((Get-Item $zip).Length / 1KB))
