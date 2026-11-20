# Isolated WinForms controls and local stand-in CLIs. No provider accounts or network.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$root = Split-Path -Parent $PSScriptRoot
$asm = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
function Record($name) { [Activator]::CreateInstance($asm.GetType('StickyPro.' + $name)) }
function Assert($ok,$message) { if (!$ok) { throw $message } }
$fixture = [IO.Directory]::CreateDirectory((Join-Path $root ('build-test\chat-' + [guid]::NewGuid().ToString('N')))).FullName
$asm.GetType('StickyPro.Store').GetField('Dir',$static).SetValue($null,$fixture)
$agentStore = $asm.GetType('StickyPro.AgentStore')
$defs = $agentStore.GetProperty('Defs',$static).GetValue($null,$null)
$scriptFile = Join-Path $fixture 'member.ps1'
@'
param($member)
$prompt = [Console]::In.ReadToEnd()
if ($member -eq 'broken') { Write-Output 'PARTIAL_OUTPUT'; exit 1 }
if ($prompt.Contains('You chair a model council')) {
    if ($prompt.Contains('PARTIAL_OUTPUT') -or !$prompt.Contains('completed-first') -or !$prompt.Contains('completed-second')) { exit 2 }
    Write-Output 'fixture verdict'
} else { Write-Output ('completed-' + $member) }
'@ | Set-Content -LiteralPath $scriptFile -Encoding UTF8
$chat = Record AgentChat
$chat.Id = 'test-council'; $chat.Council = $true; $chat.Rounds = 1; $chat.Folder = $fixture
foreach ($id in @('broken','first','second')) {
    $def = Record AgentDef; $def.Id = $id; $def.Name = $id; $def.Stdin = $true
    $def.Exe = Join-Path $PSHOME 'powershell.exe'; $def.Args = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $scriptFile + '" ' + $id
    $defs.Add($def)
    $member = Record CouncilMember; $member.Agent = $id; $member.Key = $id
    $chat.Members.Add($member)
}
$chat.Chair = 'broken'
$dlg = [Activator]::CreateInstance($asm.GetType('StickyPro.CouncilDialog'),@($chat.PSObject.BaseObject))
try {
    $grid = $dlg.GetType().GetField('grid',$instance).GetValue($dlg)
    $grid.CurrentCell = $grid.Rows[0].Cells[1]
    $dlg.GetType().GetMethod('MoveRow',$instance).Invoke($dlg,@(1))
    $dlg.GetType().GetMethod('Save',$instance).Invoke($dlg,@())
    Assert ($chat.Chair -eq 'broken' -and $chat.Members[1].Agent -eq 'broken') 'Moving a member reassigned the chair'
    $grid.Rows[1].Cells[0].Value = $false
    $dlg.GetType().GetMethod('Save',$instance).Invoke($dlg,@())
    Assert ($chat.Chair -eq '') 'Disabled council member remained chair'
    $chat.Members[1].Off = $false; $chat.Chair = 'broken'
} finally { $dlg.Dispose() }
$pane = [Activator]::CreateInstance($asm.GetType('StickyPro.ChatPane'),@($chat.PSObject.BaseObject))
try {
    $null = $pane.Handle
    $pane.Send('Fixture question')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($pane.Busy -and $timer.Elapsed.TotalSeconds -lt 45) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 }
    Assert (!$pane.Busy) 'Council failed to finish'
    Assert (@($chat.Messages | Where-Object { $_.Label -like 'Verdict*' -and $_.Text -eq 'fixture verdict' -and $_.Agent -eq 'first' }).Count -eq 1) 'Verdict missing, or failed chair or partial output used'
    Assert (@($chat.Messages | Where-Object { $_.Text -like 'No answer from broken*' }).Count -eq 1) 'Failed member was not identified'
    $view = $pane.GetType().GetField('view',$instance).GetValue($pane)
    Assert ($view.FindMessage('completed-first',0) -eq '1 / 2') 'First transcript match missing'
    Assert ($view.FindMessage('COMPLETED-FIRST',1) -eq '2 / 2') 'Case-insensitive next match failed'
    Assert ($view.FindMessage('completed-first',1) -eq '1 / 2') 'Find failed to wrap'
    Assert ($view.FindMessage('no-such-message',0) -eq '0 matches') 'Missing search result retained a selection'
    $view.JumpToVerdict()
    Assert ($view.GetType().GetField('foundMessage',$instance).GetValue($view).Text -eq 'fixture verdict') 'Verdict navigation missed the final answer'
    $input = $pane.GetType().GetField('input',$instance).GetValue($pane); $input.Text = 'Existing draft'
    $pane.GetType().GetMethod('OnCollab',$instance).Invoke($pane,@('edit','Fixture question'))
    Assert ($input.Text.StartsWith('Existing draft') -and $input.SelectedText -eq 'Fixture question') 'Editing an earlier question damaged the draft'
    $chat.Sessions['first'] = 'old-session'
    $cut = 0
    for ($i = 0; $i -lt $chat.Messages.Count; $i++) { if ($chat.Messages[$i].Text -eq 'completed-first') { $cut = $i + 1; break } }
    Assert ($cut -gt 0) 'Branch fixture missing'
    $branch = $agentStore.GetMethod('CopyThrough',$static).Invoke($null,@($chat.PSObject.BaseObject,$cut))
    Assert ($branch.Messages.Count -eq $cut -and $branch.Sessions.Count -eq 0 -and $branch.Chair -eq $chat.Chair) 'Branch retained later history or old session, or lost chair'
    $ask = Record AgentMsg; $ask.Role = 'user'; $ask.Text = 'Follow-up question'; $branch.Messages.Add($ask)
    $prompt = $agentStore.GetMethod('PromptFor',$static).Invoke($null,@($branch,'first'))
    Assert ($prompt.Contains('completed-first') -and $prompt.Contains('Follow-up question') -and !$prompt.Contains('fixture verdict')) 'First branch turn received incorrect context'
    $pane.SetChat($branch); Assert ($input.Text -eq '') 'Draft leaked to a branch'
    $pane.SetChat($chat); Assert ($input.Text.Contains('Existing draft')) 'Switching chats lost the draft'
} finally { $pane.Dispose() }
Write-Host 'AGENT CONVERSATIONS OK: search, draft editing, independent branch, partial council failure, fallback chair and verdict'
