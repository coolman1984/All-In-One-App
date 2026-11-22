# Uses only isolated fixtures and hidden controls; never loads production data.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$instance = [Reflection.BindingFlags]'Public,NonPublic,Instance'
function Assert($value, [string]$message) { if (-not $value) { throw $message } }
function Call([string]$type, [string]$method, [object[]]$arguments) {
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        if ($null -ne $arguments[$i]) { $arguments[$i] = $arguments[$i].PSObject.BaseObject }
    }
    $assembly.GetType('StickyPro.' + $type).GetMethod($method, $static).Invoke($null, $arguments)
}
function Record([string]$type) { [Activator]::CreateInstance($assembly.GetType('StickyPro.' + $type)) }
$fixture = Join-Path $root ('build-test\reliability-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$store = $assembly.GetType('StickyPro.Store')
$store.GetField('Dir', $static).SetValue($null, $fixture)
$store.GetField('ImgDir', $static).SetValue($null, $fixture)

# Mutating nested originals after taking a snapshot must not change the queued save.
$settings = Record 'Settings'
$workspace = Record 'TermWorkspace'; $pane = Record 'TermPane'; $pane.Folder = 'original'
$workspace.Panes.Add($pane); $settings.TermWorkspaces.Add($workspace)
$snapshot = Call 'DataSnapshot' 'Copy' @($settings)
$pane.Folder = 'changed'; $workspace.Panes.Clear(); $settings.Hotkeys['test'] = 'changed'
Assert ($snapshot.TermWorkspaces[0].Panes[0].Folder -eq 'original') 'Snapshot shares nested workspace data.'
Assert (-not $snapshot.Hotkeys.ContainsKey('test')) 'Snapshot shares settings dictionary.'
$task = Record 'TaskItem'; $step = Record 'SubTask'; $step.Text = 'original'; $task.Steps.Add($step)
$snapshot = Call 'DataSnapshot' 'Copy' @($task)
$step.Done = $true; $task.Steps.Clear()
Assert ($snapshot.Steps.Count -eq 1 -and -not $snapshot.Steps[0].Done) 'Snapshot shares mutable task steps.'
$prompt = Record 'Prompt'; $version = Record 'PromptVersion'; $version.Body = 'original'; $prompt.Versions.Add($version)
$snapshot = Call 'DataSnapshot' 'Copy' @($prompt)
$version.Body = 'changed'; $prompt.Versions.Clear()
Assert ($snapshot.Versions[0].Body -eq 'original') 'Snapshot shares prompt versions.'

# Construction should allocate only Home, preserve loaded pages, and ignore unknown keys.
$form = Record 'MainForm'
try {
    $loaded = $form.GetType().GetField('loadedPages', $instance).GetValue($form)
    Assert ($loaded.Count -eq 1 -and $loaded.ContainsKey('dashboard')) 'MainForm eagerly loaded unused pages.'
    $form.ShowPage('notes'); $notes = $loaded['notes']; $form.ShowPage('dashboard'); $form.ShowPage('notes')
    Assert ([object]::ReferenceEquals($notes, $loaded['notes'])) 'Navigation discarded page state.'
    $count = $loaded.Count; $form.ShowPage('unknown'); Assert ($loaded.Count -eq $count -and $form.Page -eq 'notes') 'Unknown page changed navigation.'
    $form.CommitEdits(); $form.UpdatePaused(); $form.RefreshLaunchers()
} finally { $form.Dispose() }
Assert (-not [IO.Directory]::Exists((Join-Path $fixture 'agents'))) 'Unused Agents page created data.'

# Large overdue gaps, weekends, month ends and exhausted dates.
$now = [datetime]'2026-11-15T12:00:00'
foreach ($repeat in @('daily','weekly','weekdays','monthly','yearly')) {
    $next = [long](Call 'Store' 'NextOccurrenceAt' @(([datetime]'1900-01-31T09:00:00').Ticks, $repeat, $now))
    Assert ($next -gt $now.Ticks) ('Recurrence failed to skip overdue occurrences: ' + $repeat)
}
$friday = [datetime]'2026-11-13T09:00:00'
$next = [datetime]::new([long](Call 'Store' 'NextOccurrenceAt' @($friday.Ticks, 'weekdays', $friday)))
Assert ($next.DayOfWeek -eq 'Monday' -and $next.Hour -eq 9) 'Weekday recurrence did not skip the weekend.'
$next = [datetime]::new([long](Call 'Store' 'NextOccurrenceAt' @(([datetime]'2026-01-31T09:00:00').Ticks, 'monthly', ([datetime]'2026-03-01'))))
Assert ($next.Day -eq 31 -and $next.Month -eq 3) 'Monthly skip drifted from the anchor day.'
Assert ((Call 'Store' 'NextOccurrenceAt' @([datetime]::MaxValue.Ticks, 'daily', $now)) -eq [datetime]::MaxValue.Ticks) 'Date limit did not stop safely.'

# Backup includes nested agents and payloads; staging does not replace current data.
$live = Join-Path $fixture 'live'; $backup = Join-Path $fixture 'backup'; $copy = Join-Path $fixture 'copy'
[IO.Directory]::CreateDirectory((Join-Path $backup 'agents\chats')) | Out-Null
[IO.Directory]::CreateDirectory($live) | Out-Null
[IO.File]::WriteAllText((Join-Path $live 'notes.json'), '[{"Text":"current"}]')
[IO.File]::WriteAllText((Join-Path $backup 'notes.json'), '[{"Text":"restored"}]')
[IO.File]::WriteAllText((Join-Path $backup 'agents\chats\chat.json'), '{"Title":"saved chat"}')
[IO.File]::WriteAllBytes((Join-Path $backup 'agents\payload.bin'), [byte[]]@(1,2,3,4))
Call 'Backup' 'CopyData' @($backup, $copy)
Assert ([IO.File]::ReadAllBytes((Join-Path $copy 'agents\payload.bin')).Length -eq 4) 'Backup omitted nested data.'
$blocked = $false
try { Call 'DataFiles' 'CopyTree' @($backup, (Join-Path $backup 'nested')) } catch { $blocked = $true }
Assert $blocked 'Recursive backup destination was accepted.'
Call 'Backup' 'Stage' @($backup, $live)
Assert ([IO.File]::ReadAllText((Join-Path $live 'notes.json')) -like '*current*') 'Staging overwrote current data.'
Call 'Backup' 'ApplyPending' @($live)
Assert ([IO.File]::ReadAllText((Join-Path $live 'notes.json')) -like '*restored*') 'Restore did not install staged data.'
Assert ([IO.File]::Exists((Join-Path $live 'agents\chats\chat.json'))) 'Restore lost agent chats.'
$previous = @(Get-ChildItem -LiteralPath $fixture -Directory -Filter 'live.before-restore-*')
Assert ($previous.Count -eq 1) 'Restore did not retain the previous folder.'
Assert ([IO.File]::ReadAllText((Join-Path $previous[0].FullName 'notes.json')) -like '*current*') 'Restore damaged the rollback copy.'
[IO.File]::WriteAllText((Join-Path $backup 'notes.json'), '{broken')
$blocked = $false
try { Call 'Backup' 'Stage' @($backup, $live) } catch { $blocked = $true }
Assert $blocked 'Malformed backup was accepted.'
Assert ([IO.File]::ReadAllText((Join-Path $live 'notes.json')) -like '*restored*') 'Failed restore modified live data.'

# A process interruption between directory renames recovers the previous data on next startup.
$interrupted = Join-Path $fixture 'interrupted'
$recovery = $interrupted + '.before-restore-fixture'
[IO.Directory]::CreateDirectory($recovery) | Out-Null
[IO.File]::WriteAllText((Join-Path $recovery 'notes.json'), '[{"Text":"recovered"}]')
[IO.File]::WriteAllText(($interrupted + '.restore-location'), $recovery)
Call 'Backup' 'ApplyPending' @($interrupted)
Assert ([IO.File]::ReadAllText((Join-Path $interrupted 'notes.json')) -like '*recovered*') 'Interrupted restore did not recover the previous folder.'
Assert (-not [IO.File]::Exists(($interrupted + '.restore-location'))) 'Recovery left a stale transaction marker.'

# Coalesced agent saves preserve the newest revision, flush on demand, and cannot resurrect deletion.
$chat = Record 'AgentChat'; $chat.Id = 'fixture-chat'
$message = Record 'AgentMsg'; $message.Text = 'first'; $chat.Messages.Add($message)
for ($i = 0; $i -lt 30; $i++) { $message.Text = 'revision-' + $i; Call 'AgentStore' 'Save' @($chat) }
Assert (Call 'AgentStore' 'FlushWrites' @()) 'Agent writer failed to flush.'
$chatPath = Join-Path $fixture 'agents\chats\fixture-chat.json'
Assert ([IO.File]::ReadAllText($chatPath) -like '*revision-29*') 'An older agent save replaced the latest revision.'
Call 'AgentStore' 'Save' @($chat); Call 'AgentStore' 'Delete' @($chat)
Assert (Call 'AgentStore' 'FlushWrites' @()) 'Agent deletion failed to flush.'
Assert (-not [IO.File]::Exists($chatPath)) 'A queued save resurrected a deleted chat.'

# A deferred move copies the last committed state and leaves the old data in place.
$source = Join-Path $fixture 'move-source'; $target = Join-Path $fixture 'move-target'
$config = Join-Path $fixture 'config'
[IO.Directory]::CreateDirectory($source) | Out-Null
[IO.Directory]::CreateDirectory($config) | Out-Null
[IO.File]::WriteAllText((Join-Path $source 'notes.json'), '[]')
[IO.File]::WriteAllText((Join-Path $source 'removed.txt'), 'old')
$store.GetField('dataRoot', $static).SetValue($null, $config)
$store.GetField('Dir', $static).SetValue($null, $source)
$store.GetField('DirOverride', $static).SetValue($null, $null)
$location = Join-Path $config 'data-location.txt'
$errorText = Call 'Store' 'MoveDataTo' @((Join-Path $source 'nested'))
Assert ($null -ne $errorText) 'Migration accepted a descendant destination.'
$errorText = Call 'Store' 'MoveDataTo' @($backup)
Assert ($null -ne $errorText) 'Migration accepted an occupied destination.'
Assert (-not [IO.File]::Exists($location)) 'Rejected migration changed the location.'

Assert ($null -eq (Call 'Store' 'MoveDataTo' @($target))) 'Could not schedule the move.'
[IO.Directory]::CreateDirectory($target) | Out-Null
[IO.File]::WriteAllText((Join-Path $target 'other.txt'), 'leave it')
Assert ((Call 'Store' 'ApplyPendingMove' @($source)) -eq $source) 'A now-occupied target replaced the source.'
Assert ([IO.File]::ReadAllText((Join-Path $target 'other.txt')) -eq 'leave it') 'Migration overwrote an unrelated file.'
Assert (-not [IO.File]::Exists($location)) 'A failed copy changed the location.'
[IO.File]::Delete((Join-Path $target 'other.txt'))

Assert ($null -eq (Call 'Store' 'MoveDataTo' @($target))) 'Could not reschedule the move.'
Assert (-not [IO.File]::Exists($location)) 'Scheduling the move changed the active folder.'
[IO.File]::WriteAllText((Join-Path $source 'notes.json'), '[{"Text":"after scheduling"}]')
[IO.File]::WriteAllText((Join-Path $source 'created.txt'), 'new')
[IO.File]::Delete((Join-Path $source 'removed.txt'))
Assert ((Call 'Store' 'ApplyPendingMove' @($source)) -eq $target) 'Applying the pending move did not return its target.'
Assert ([IO.File]::ReadAllText((Join-Path $target 'notes.json')) -like '*after scheduling*') 'Migration missed a late edit.'
Assert ([IO.File]::Exists((Join-Path $target 'created.txt'))) 'Migration missed a late file creation.'
Assert (-not [IO.File]::Exists((Join-Path $target 'removed.txt'))) 'Migration copied a deleted file.'
Assert ([IO.File]::ReadAllText($location) -eq $target) 'Migration did not commit the new location.'
Assert ([IO.File]::Exists((Join-Path $source 'notes.json'))) 'Migration removed the original data.'

# A locked primary with a readable older backup must never look like an empty or recovered profile.
$settingsPath = Join-Path $source 'settings.json'
[IO.File]::WriteAllText($settingsPath, '{"UserName":"newer"}')
[IO.File]::WriteAllText($settingsPath + '.bak', '{"UserName":"older"}')
$read = $store.GetMethod('Read', $static).MakeGenericMethod(@($assembly.GetType('StickyPro.Settings')))
$locked = [IO.File]::Open($settingsPath, 'Open', 'Read', 'None')
try {
    $blocked = $false
    try { $null = $read.Invoke($null, @('settings.json')) } catch { $blocked = $true }
    Assert $blocked 'A temporarily locked primary was treated as missing or invalid.'
} finally { $locked.Dispose() }
Assert ($read.Invoke($null, @('settings.json')).UserName -eq 'newer') 'The primary did not recover after unlocking.'
[IO.File]::WriteAllText($settingsPath, '{broken')
Assert ($read.Invoke($null, @('settings.json')).UserName -eq 'older') 'A corrupt primary did not recover from its readable backup.'
Write-Host 'RELIABILITY OK: snapshots, lazy pages, recurrence, backup/restore, agent saves, deferred migration and locked data'
