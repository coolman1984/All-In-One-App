# Isolated providers and temporary profile folders. Does not launch a real sign-in or attach to a real pane.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$root = Split-Path -Parent $PSScriptRoot
$a = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
function New-Record($name) { [Activator]::CreateInstance($a.GetType('StickyPro.' + $name)) }
function Assert($ok,$message) { if (!$ok) { throw $message } }
function Wait-Until($test,$message) {
    $timer=[Diagnostics.Stopwatch]::StartNew()
    do { [Windows.Forms.Application]::DoEvents(); if (& $test) { return }; Start-Sleep -Milliseconds 15 } while ($timer.Elapsed.TotalSeconds -lt 40)
    throw $message
}
$fixture = [IO.Directory]::CreateDirectory((Join-Path $root ('build-test\leader-' + [guid]::NewGuid().ToString('N')))).FullName
$a.GetType('StickyPro.Store').GetField('Dir',$static).SetValue($null,$fixture)
$accounts = $a.GetType('StickyPro.AgentAccounts')
foreach ($provider in @('codex','claude')) {
    $envKey = if ($provider -eq 'codex') {'CODEX_HOME'} else {'CLAUDE_CONFIG_DIR'}
    foreach ($name in @('first','second')) {
        $psi = New-Object Diagnostics.ProcessStartInfo
        $psi.UseShellExecute = $false; $psi.EnvironmentVariables['ANTHROPIC_AUTH_TOKEN'] = 'fixture'; $psi.EnvironmentVariables['OPENAI_API_KEY'] = 'fixture'
        $dir = Join-Path $fixture $provider
        $accounts.GetMethod('Configure',$static).Invoke($null,@($psi.PSObject.BaseObject,$provider,$name,[string]$dir,$true))
        Assert ($psi.EnvironmentVariables[$envKey] -eq (Join-Path $dir $name)) 'Provider account home was not isolated'
        $secret = if ($provider -eq 'codex') { 'OPENAI_API_KEY' } else { 'ANTHROPIC_AUTH_TOKEN' }
        Assert (!$psi.EnvironmentVariables.ContainsKey($secret)) 'Named account inherited a competing credential'
    }
}
foreach ($bad in @('../outside','C:\unsafe','con','a b')) { Assert (!$accounts.GetMethod('Valid').Invoke($null,@($bad))) 'Unsafe profile name accepted' }
$session=$accounts.GetMethod('Session')
Assert ($session.Invoke($null,@('codex','')) -eq 'codex' -and $session.Invoke($null,@('codex','first')) -ne $session.Invoke($null,@('codex','second'))) 'Session keys overlap'
$decode=$a.GetType('StickyPro.HerdrScreen').GetMethod('Decode',$static)
Assert ($decode.Invoke($null,@('p-1','{"result":{"pane_id":"p-1","text":"visible worker output"}}')) -eq 'visible worker output') 'Screen decoding failed'
$rejected=$false
try { $null=$decode.Invoke($null,@('p-2','{"result":{"pane_id":"p-1","text":"other pane"}}')) } catch { $rejected=$true }
Assert $rejected 'Another pane was attached'
$chat=New-Record AgentChat
$chat.Id='leader-fixture'; $chat.Folder=$fixture; $chat.LeaderTeam=$true; $chat.Council=$true; $chat.Chair='planner'
$scriptFile=Join-Path $fixture 'worker.ps1'
@'
param($role)
$message=[Console]::In.ReadToEnd()
if($role -eq 'planner') {
    if($message.Contains('verified-worker-result') -and $message.Contains('attached-visible-output')) { '{"final":"Synthesis from worker and screen"}' }
    else { '{"tasks":[{"worker":1,"task":"Check fixture"}]}' }
} else {
    if(!$message.Contains('Check fixture')) { exit 1 }
    'starting-fixture'
    Start-Sleep -Milliseconds 800
    'verified-worker-result'
}
'@ | Set-Content -LiteralPath $scriptFile -Encoding UTF8
$defs=$a.GetType('StickyPro.AgentStore').GetProperty('Defs',$static).GetValue($null,$null)
foreach ($id in @('planner','worker')) {
    $d=New-Record AgentDef; $d.Id=$id; $d.Name=$id; $d.Stdin=$true; $d.Exe=Join-Path $PSHOME 'powershell.exe'
    $d.Args='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$scriptFile+'" '+$id
    $defs.Add($d); $member=New-Record CouncilMember; $member.Key=$id; $member.Agent=$id; $chat.Members.Add($member)
}
$pane=[Activator]::CreateInstance($a.GetType('StickyPro.ChatPane'),@($chat.PSObject.BaseObject))
try {
    $type=$pane.GetType(); $null=$pane.Handle; $pane.Send('Test team')
    $workers=$type.GetField('TeamWorkers',$instance).GetValue($pane)
    Wait-Until { $workers.Count -eq 1 -and $workers[0].Proposed } 'Leader did not propose a task'
    Assert ($null -eq $workers[0].Run) 'Worker started before plan dispatch'
    $desk=[Activator]::CreateInstance($a.GetType('StickyPro.LeaderDesk'),@($pane.PSObject.BaseObject))
    try { Assert (!$desk.Visible) 'Test opened a window' } finally { $desk.Dispose() }
    $workers[0].Attached='attached-visible-output'
    $type.GetMethod('DispatchPlan',$instance).Invoke($pane,@())
    Wait-Until { $workers[0].Output.Contains('starting-fixture') -and $null -ne $workers[0].Run } 'Plain CLI output was unavailable while running'
    Wait-Until { !$pane.Busy } 'Leader did not finish after workers'
    Assert ($chat.Messages[$chat.Messages.Count-1].Text -eq 'Synthesis from worker and screen') 'Leader did not receive worker output and attached pane'
    $pane.Send('Second turn')
    $workers=$type.GetField('TeamWorkers',$instance).GetValue($pane)
    Wait-Until { $workers.Count -gt 0 -and $workers[0].Proposed } 'Leader could not start another turn'
    $type.GetMethod('DispatchWorker',$instance).Invoke($pane,@($workers[0],'Check fixture'))
    $type.GetMethod('StopWorker',$instance).Invoke($pane,@($workers[0]))
    Wait-Until { !$workers[0].Run -and $workers[0].Status -eq 'Stopped' } 'Stopping the selected worker failed'
    Assert $pane.Busy 'Stopping one worker unexpectedly closed the team'
    $pane.Stop()
    Assert (!$pane.Busy) 'Stopping the team left the conversation busy'
} finally { $pane.Dispose() }
Write-Host 'LEADER TEAM OK: isolated profiles, scoped sessions, HERDR decoding, plan review, live worker and final synthesis'
