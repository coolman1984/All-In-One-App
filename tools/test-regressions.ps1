# Run with Windows PowerShell 5.1 after tools/test-build.ps1. Loads types only;
# never invokes Program.Main, Store.Load, the running app, clipboard, or registry.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
function Assert($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Invoke-Static([string]$type, [string]$method, [object[]]$arguments) {
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        if ($null -ne $arguments[$i]) { $arguments[$i] = $arguments[$i].PSObject.BaseObject }
    }
    $assembly.GetType("StickyPro.$type").GetMethod($method, $flags).Invoke($null, $arguments)
}
function Set-Store([string]$name, $value) {
    $assembly.GetType('StickyPro.Store').GetField($name, $flags).SetValue($null, $value)
}
function Get-Store([string]$name) {
    $assembly.GetType('StickyPro.Store').GetField($name, $flags).GetValue($null)
}

$fixture = Join-Path $root ('build-test\regression-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
Set-Store 'Dir' $fixture
Set-Store 'ImgDir' $fixture

# Force a failed write without touching any existing file, then retry successfully.
$blockedTemp = Join-Path $fixture 'notes.json.tmp'
[IO.Directory]::CreateDirectory($blockedTemp) | Out-Null
Set-Store 'notesDirty' $true
Invoke-Static 'Store' 'SaveNow' @()
Assert (Get-Store 'notesDirty') 'Failed notes save lost its pending changes.'
[IO.Directory]::Delete($blockedTemp) # known empty test directory only
Invoke-Static 'Store' 'SaveNow' @()
Assert (-not (Get-Store 'notesDirty')) 'Successful retry left notes dirty.'
Assert ([IO.File]::ReadAllText((Join-Path $fixture 'notes.json')) -eq '[]') 'Retry did not persist notes.'

# Both files share one dirty flag: neither failure may drop pending category edits.
$blockedTemp = Join-Path $fixture 'tagcats.json.tmp'
[IO.Directory]::CreateDirectory($blockedTemp) | Out-Null
Set-Store 'taggedDirty' $true
Invoke-Static 'Store' 'SaveNow' @()
Assert (Get-Store 'taggedDirty') 'Failed category save lost pending changes.'
[IO.Directory]::Delete($blockedTemp)
Invoke-Static 'Store' 'SaveNow' @()
Assert (-not (Get-Store 'taggedDirty')) 'Tagged/category retry did not finish.'

# Ensure the ABI layouts used by the x64 build match the Windows structures.
foreach ($case in @(
    @('StickyPro.Native+KBDLLHOOKSTRUCT', 24),
    @('StickyPro.Shot+RECT', 16),
    @('StickyPro.FastFiles+WIN32_FIND_DATA', 592),
    @('StickyPro.Cleaner+SHQUERYRBINFO', 24),
    @('StickyPro.ProcessSampler+MEMSTATUS', 64)
)) {
    $type = $assembly.GetType($case[0])
    $instance = [Activator]::CreateInstance($type, $true)
    Assert ([Runtime.InteropServices.Marshal]::SizeOf($instance) -eq $case[1]) ("ABI mismatch: " + $case[0])
}

# Round paths must tolerate collapsed/narrow controls during layout.
$ui = $assembly.GetType('StickyPro.Ui')
$round = $ui.GetMethod('Round', $flags)
foreach ($size in @(0, 1, 3)) {
    $rect = New-Object Drawing.Rectangle(0, 0, $size, $size)
    $path = $round.Invoke($null, @($rect.PSObject.BaseObject, 8))
    $path.Dispose()
}

# All documented defaults parse; unknown tokens and multiple main keys must fail.
$hotkeys = $assembly.GetType('StickyPro.Hotkeys')
$parse = $hotkeys.GetMethod('Parse', $flags)
foreach ($action in $hotkeys.GetField('Actions', $flags).GetValue($null)) {
    $arguments = [object[]]@($action[2], [uint32]0, [Windows.Forms.Keys]::None)
    Assert ($parse.Invoke($null, $arguments)) ('Default shortcut rejected: ' + $action[2])
}
foreach ($invalid in @('Ctrl+Bogus+N', 'Ctrl+N+V', 'Ctrl+ShiftKey', 'Ctrl+999999')) {
    $arguments = [object[]]@($invalid, [uint32]0, [Windows.Forms.Keys]::None)
    Assert (-not $parse.Invoke($null, $arguments)) ('Invalid shortcut accepted: ' + $invalid)
}

# Stale queue entries may be copied, but must not resurrect deleted history.
$clip = [Activator]::CreateInstance($assembly.GetType('StickyPro.Clip'))
$clip.Id = 'removed-fixture-clip'
$clip.Text = 'A queued text item removed from history'
Invoke-Static 'Store' 'TouchClip' @($clip)
Assert ((Get-Store 'Clips').Count -eq 0) 'Touching a removed queued clip resurrected it.'

# Defective imported timestamp values must not crash rendering.
[void](Invoke-Static 'Ui' 'Ago' @([long]::MaxValue))

Write-Host 'REGRESSIONS OK: save retry, paired categories, x64 layouts, tiny paths, hotkeys, removed clips, invalid timestamps'
