# Isolated component benchmark. Never starts Program.Main, shows windows, or reads user data.
param([string]$AssemblyPath = '', [string]$OutputPath = '', [int]$Iterations = 3)
$ErrorActionPreference = 'Stop'
if ($Iterations -lt 1 -or $Iterations -gt 20) { throw 'Iterations must be 1..20.' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$root = Split-Path -Parent $PSScriptRoot
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $root 'build-test\StickyNotesPro.test.exe' }
$assembly = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($AssemblyPath))
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$store = $assembly.GetType('StickyPro.Store')
$fixture = Join-Path $root ('build-test\measure-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$store.GetField('Dir', $flags).SetValue($null, $fixture)
$store.GetField('ImgDir', $flags).SetValue($null, $fixture)
$store.GetField('Settings', $flags).GetValue($null).SnipFolder = $fixture
$samples = @()
for ($i = 0; $i -lt $Iterations; $i++) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $form = [Activator]::CreateInstance($assembly.GetType('StickyPro.MainForm'), $true)
    try {
        $construct = $watch.Elapsed.TotalMilliseconds
        $watch.Restart()
        $form.Size = New-Object Drawing.Size(1120, 700)
        $form.PerformLayout()
        $samples += [pscustomobject]@{ Iteration=$i+1; ConstructMs=[math]::Round($construct,2); LayoutMs=[math]::Round($watch.Elapsed.TotalMilliseconds,2) }
    } finally { $form.Dispose() }
}
$process = [Diagnostics.Process]::GetCurrentProcess()
try {
    $report = [pscustomobject]@{
        Scope='Hidden MainForm construction and layout in Windows PowerShell; not live app idle usage'
        AssemblySHA256=(Get-FileHash $AssemblyPath -Algorithm SHA256).Hash
        Samples=$samples
        HostPrivateBytes=$process.PrivateMemorySize64
        ManagedBytes=[GC]::GetTotalMemory($false)
        HostHandles=$process.HandleCount
    }
    $json = $report | ConvertTo-Json -Depth 5
    if ($OutputPath) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json) }
    $json
} finally { $process.Dispose() }
