$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$instance = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$store = $assembly.GetType('StickyPro.Store')
function Add-Record([string]$collection, [string]$type, [hashtable]$values) {
    $record = [Activator]::CreateInstance($assembly.GetType('StickyPro.' + $type))
    foreach ($key in $values.Keys) { $record.$key = $values[$key] }
    $store.GetField($collection, $static).GetValue($null).Add($record)
}
function Assert($value, [string]$message) { if (-not $value) { throw $message } }
$today = [datetime]::Today.Ticks
Add-Record 'Clips' 'Clip' @{Time=$today; Text='today'}
Add-Record 'Clips' 'Clip' @{Time=($today - 1); Text='yesterday'}
Add-Record 'Prompts' 'Prompt' @{Title='Most used'; Uses=12}
Add-Record 'Prompts' 'Prompt' @{Title='Other'; Uses=2}
Add-Record 'Notes' 'Note' @{RemindAt=($today + [timespan]::TicksPerHour)}
Add-Record 'Activity' 'ActivityRecord' @{Path='fixture.txt'; Event='Opened'; Time=$today}
Add-Record 'Activity' 'ActivityRecord' @{Path='FIXTURE.txt'; Event='Opened'; Time=$today}
Add-Record 'Activity' 'ActivityRecord' @{Path='closed.txt'; Event='Closed'; Time=$today}
Add-Record 'Tagged' 'TaggedFile' @{Category='urgent'}
$facts = $assembly.GetType('StickyPro.DashboardFacts').GetMethod('Read', $static).Invoke($null, @([long]$today))
Assert ($facts.ClipsToday -eq 1 -and $facts.PromptCount -eq 2 -and $facts.RemindersToday -eq 1 -and $facts.FilesToday -eq 1 -and $facts.Urgent -eq 1) 'Dashboard counts or day boundaries are wrong.'
Assert ($facts.MostUsed -like 'Most used*') 'Most-used prompt is wrong.'
$sampler = $assembly.GetType('StickyPro.ProcessSampler')
$history = New-Object 'Collections.Generic.Queue[double]'
for ($i=0; $i -lt 100; $i++) { [void]$sampler.GetMethod('PushHistory', $static).Invoke($null, @($history.PSObject.BaseObject, [double]$i)) }
Assert ($history.Count -eq 60 -and $history.Peek() -eq 40) 'Sparkline history is not bounded to 60 samples.'
$ui = $assembly.GetType('StickyPro.Ui')
$ui.GetMethod('SetDesignFlags', $static).Invoke($null, @('flow'))
$board = [Activator]::CreateInstance($assembly.GetType('StickyPro.NexusBoard'), $true)
try {
    foreach ($width in @(240, 700, 1000)) {
        [void]$board.Measure($width)
        $week = $board.GetType().GetField('weekCard', $instance).GetValue($board)
        $disk = $board.GetType().GetField('storage', $instance).GetValue($board)
        $stats = $board.GetType().GetField('kpiCards', $instance).GetValue($board)
        Assert ($week.Right -le $width -and $week.Bottom -lt $stats[0].Top -and $stats[-1].Bottom -lt $disk.Top) "Daily work is not first at $width px."
    }
} finally { $board.Dispose() }
$panel = [Activator]::CreateInstance($assembly.GetType('StickyPro.DashboardPanel'), $true)
try {
    foreach ($width in @(240,700,1000,1600)) {
        $panel.Size = New-Object Drawing.Size($width, 700)
        $panel.PerformLayout()
        foreach ($child in $panel.Controls) { $child.PerformLayout(); foreach ($inner in $child.Controls) { $inner.PerformLayout() } }
        if ($panel.HorizontalScroll.Visible) { throw "Dashboard needs horizontal scrolling at $width px." }
    }
    Assert ($sampler.GetField('watchers', $static).GetValue($null).Count -eq 0) 'Hidden dashboard started live sampling.'
} finally { $panel.Dispose() }
Write-Host 'DASHBOARD OK: daily counters, distinct files, most-used prompt, 60-sample bound, responsive grid, hidden sampling idle'
