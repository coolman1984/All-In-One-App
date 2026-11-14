# Compile check for reviewers: builds to build-test\ only. Never touches the running app or data\.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out  = Join-Path $root 'build-test'
New-Item -ItemType Directory -Force $out | Out-Null
$src  = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll'
$outArg = '/out:' + (Join-Path $out 'AllInOne.test.exe')
$icoArg = '/win32icon:' + (Join-Path $root 'src\app.ico')
& $csc /nologo /target:winexe /optimize+ /platform:x64 $outArg $icoArg $refs $src
if ($LASTEXITCODE -ne 0) { Write-Error 'BUILD FAILED'; exit 1 }
Write-Host 'BUILD OK'
