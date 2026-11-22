# End-to-end local scheduling with disposable tools and a stand-in Assistant process.
param([string]$AssemblyPath = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
$root = Split-Path -Parent $PSScriptRoot
if (!$AssemblyPath) { $AssemblyPath = Join-Path $root 'build-test\StickyNotesPro.test.exe' }
$asm = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($AssemblyPath))
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$store = $asm.GetType('StickyPro.Store', $true)
$cron = $asm.GetType('StickyPro.CronEngine', $true)
$memory = $asm.GetType('StickyPro.AgentMemory', $true)
$vault = $asm.GetType('StickyPro.SqliteVault', $true)
$agents = $asm.GetType('StickyPro.AgentStore', $true)
$api = $asm.GetType('StickyPro.ApiTools', $true)
$json = New-Object Web.Script.Serialization.JavaScriptSerializer
$dir = [IO.Directory]::CreateDirectory((Join-Path $root ('build-test\cron-' + [Guid]::NewGuid().ToString('N')))).FullName
$store.GetField('Dir', $static).SetValue($null, $dir)

function Assert($condition, [string]$message) { if (!$condition) { throw $message } }
function Tool([string]$name, [hashtable]$argsMap) {
    $arguments = New-Object 'Collections.Generic.Dictionary[string,object]'
    foreach ($key in $argsMap.Keys) { $arguments[$key] = $argsMap[$key].PSObject.BaseObject }
    $response = $api.GetMethod('Run', $static).Invoke($null, [object[]]@($name, $arguments.PSObject.BaseObject))
    return ,($json.DeserializeObject($response))
}
function Get-ScheduledJob([string]$id) {
    $result = Tool 'cron_manage' @{ action = 'list' }
    return @($result['jobs'] | Where-Object { $_['id'] -eq $id })[0]
}
function Wait-ScheduledJob([string]$id) {
    $until = [datetime]::UtcNow.AddSeconds(35)
    do {
        $job = Get-ScheduledJob $id
        if ($job['run_status'] -in 'completed','failed','interrupted') { return ,$job }
        Start-Sleep -Milliseconds 80
    } while ([datetime]::UtcNow -lt $until)
    throw "Job $id did not finish."
}
function New-ScheduledJob([string]$id, [hashtable]$spec) {
    $spec['action'] = 'create'
    $spec['id'] = $id
    $spec['schedule'] = 'once:2099-01-01T08:00'
    return Tool 'cron_manage' $spec
}
function Invoke-ScheduledJob([string]$id) { return Tool 'cron_manage' @{ action = 'run_now'; id = $id } }

try {
    $window = [Activator]::CreateInstance($asm.GetType('StickyPro.CronWindow', $true), $true)
    try { Assert ($window.Text -eq 'Scheduled jobs') 'The schedules window did not construct.' }
    finally { $window.Dispose() }

    $arg = New-Object 'Collections.Generic.Dictionary[string,object]'
    $arg['amount'] = 17
    $null = Tool 'tool_create' @{ name = 'cron_fixture'; kind = 'sql'; script = 'SELECT {{amount}} AS amount' }
    $null = New-ScheduledJob 'tool' @{ tool = 'cron_fixture'; tool_args = $arg }
    Assert ((Invoke-ScheduledJob 'tool')['triggered']) 'Tool was not queued.'
    $job = Wait-ScheduledJob 'tool'
    Assert ($job['run_status'] -eq 'completed' -and $job['run_output'] -match '17' -and !$job['enabled']) 'SQL action did not return the provided value or disable the one-shot.'

    $null = New-ScheduledJob 'missing-tool' @{ tool = 'not_created' }
    $null = Invoke-ScheduledJob 'missing-tool'
    Assert ((Wait-ScheduledJob 'missing-tool')['run_status'] -eq 'failed') 'Missing registered tool reported success.'
    $null = Tool 'tool_create' @{ name = 'not_created'; kind = 'sql'; script = 'SELECT 5 AS recovered' }
    Assert ((Tool 'cron_manage' @{ action = 'retry'; id = 'missing-tool' })['triggered']) 'Explicit retry did not queue.'
    Assert ((Wait-ScheduledJob 'missing-tool')['run_status'] -eq 'completed') 'Fixed tool did not execute on retry.'

    $provider = Join-Path $dir 'provider.ps1'
    [IO.File]::WriteAllText($provider, @'
$prompt = [Console]::In.ReadToEnd()
Start-Sleep -Milliseconds 550
if ($prompt -notmatch 'Fixture request' -or $prompt -notmatch 'Fixture skill step') { exit 2 }
'Fixture provider completed both instructions.'
'@)
    $defs = $agents.GetProperty('DefsFile', $static).GetValue($null, $null)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($defs)) | Out-Null
    $fixtureProvider = @{ Id = 'fixture-provider'; Name = 'Fixture provider'; Exe = (Join-Path $PSHOME 'powershell.exe'); Format = 'text'; Stdin = $true
        Args = ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $provider + '"') }
    [IO.File]::WriteAllText($defs, $json.Serialize(@($fixtureProvider)))
    $agents.GetMethod('ReloadDefs', $static).Invoke($null, @()) | Out-Null
    $null = Tool 'skill_manage' @{ action = 'save'; slug = 'cron-fixture'; content = "# Procedure`nFixture skill step" }
    $null = New-ScheduledJob 'assistant' @{ prompt = 'Fixture request'; skill = 'cron-fixture'; agent = 'fixture-provider'; folder = $dir }
    Assert ((Invoke-ScheduledJob 'assistant')['triggered']) 'Assistant was not queued.'
    Assert ((Invoke-ScheduledJob 'assistant')['status'] -eq 'BUSY') 'An in-flight job could be queued twice.'
    $job = Wait-ScheduledJob 'assistant'
    Assert ($job['run_status'] -eq 'completed' -and $job['run_output'] -match 'completed both instructions') 'Saved prompt and skill did not reach the real fixture process.'

    $null = New-ScheduledJob 'provider-missing' @{ prompt = 'This should fail'; agent = 'does-not-exist' }
    $null = Invoke-ScheduledJob 'provider-missing'
    Assert ((Wait-ScheduledJob 'provider-missing')['run_status'] -eq 'failed') 'Missing Assistant provider reported completion.'

    $sub = Tool 'sessions_spawn' @{ cli = 'does-not-exist'; prompt = 'fixture' }
    $until = [datetime]::UtcNow.AddSeconds(10)
    do {
        $status = Tool 'sessions_status' @{ jobId = $sub['jobId'] }
        if ($status['status'] -eq 'failed') { break }
        Start-Sleep -Milliseconds 80
    } while ([datetime]::UtcNow -lt $until)
    Assert ($status['status'] -eq 'failed' -and !$status['simulated']) 'An unavailable provider returned a simulated success.'

    $cron.GetMethod('Stop', $static).Invoke($null, @()) | Out-Null
    $jobFile = $cron.GetProperty('JobsFile', $static).GetValue($null, $null)
    $list = $json.DeserializeObject([IO.File]::ReadAllText($jobFile))
    foreach ($entry in $list) {
        if ($entry['id'] -eq 'tool') { $entry['run_status'] = 'queued' }
        if ($entry['id'] -eq 'assistant') { $entry['run_status'] = 'running'; $entry['run_output'] = '' }
    }
    [IO.File]::WriteAllText($jobFile, $json.Serialize($list))
    $cron.GetMethod('Start', $static).Invoke($null, @()) | Out-Null
    Assert ((Wait-ScheduledJob 'tool')['run_status'] -eq 'completed') 'Durable queued job was not processed after restart.'
    Assert ((Get-ScheduledJob 'assistant')['run_status'] -eq 'interrupted') 'An interrupted action was reset or replayed.'

    $null = Tool 'cron_manage' @{ action = 'create'; id = 'due-by-clock'; schedule = 'once:2001-02-01T08:00'; tool = 'not_created' }
    $timerResult = Wait-ScheduledJob 'due-by-clock'
    Assert ($timerResult['run_status'] -eq 'completed' -and !$timerResult['enabled']) 'The timer did not execute the due one-shot.'

    Write-Host 'SCHEDULED JOBS OK: window, tool args, prompt + skill subprocess, one-shot, duplicate protection, retries, provider failures and restart recovery'
}
finally {
    $cron.GetMethod('Stop', $static).Invoke($null, @()) | Out-Null
    $vault.GetMethod('Close', $static).Invoke($null, @()) | Out-Null
}
