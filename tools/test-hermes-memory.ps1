# Phase 3 verification for Hermes/OpenClaw/DeepSeek Memory, Skills, Dynamic Tools (Creator Mode),
# Todo Plan, Smart Compaction, Auto Fallback, Proactive Cron/Heartbeat/Watcher, and Live Canvas Dashboards.
param([string]$AssemblyPath = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)

$root = Split-Path -Parent $PSScriptRoot
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $root 'build-test\StickyNotesPro.test.exe' }
$asm = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($AssemblyPath))
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$asm.GetType('StickyPro.WebViewHost', $true).GetMethod('Hook', $static).Invoke($null, @()) | Out-Null

function Assert($ok, [string]$msg) { if (-not $ok) { throw $msg } }
function New-Obj([string]$typeName) { [Activator]::CreateInstance($asm.GetType('StickyPro.' + $typeName, $true), $true) }
function Invoke-S($type, [string]$method, [object[]]$arguments) {
    $raw = New-Object object[] $arguments.Count
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        $v = $arguments[$i]
        if ($null -eq $v) { $raw[$i] = $null } else { $raw[$i] = $v.PSObject.BaseObject }
    }
    $res = $type.GetMethod($method, $static).Invoke($null, $raw)
    return ,$res
}
function Run-Tool([string]$name, [hashtable]$map) {
    $dict = New-Object 'Collections.Generic.Dictionary[string,object]'
    if ($null -ne $map) {
        foreach ($k in $map.Keys) { $dict[$k] = $map[$k] }
    }
    $json = [string](Invoke-S $apiTools 'Run' @($name, $dict))
    return ,($js.DeserializeObject($json))
}

$workDir = [IO.Directory]::CreateDirectory((Join-Path $root ('build-test\hermes-memory-' + [guid]::NewGuid().ToString('N')))).FullName
$storeType = $asm.GetType('StickyPro.Store', $true)
$vaultType = $asm.GetType('StickyPro.SqliteVault', $true)
$memType = $asm.GetType('StickyPro.AgentMemory', $true)
$cronType = $asm.GetType('StickyPro.CronEngine', $true)
$dashType = $asm.GetType('StickyPro.DashboardGen', $true)
$harnessType = $asm.GetType('StickyPro.AssistantHarness', $true)
$agentStore = $asm.GetType('StickyPro.AgentStore', $true)
$fallbackType = $asm.GetType('StickyPro.AgentFallback', $true)
$apiTools = $asm.GetType('StickyPro.ApiTools', $true)
$js = New-Object Web.Script.Serialization.JavaScriptSerializer
$utf8 = New-Object Text.UTF8Encoding($false)

$storeType.GetField('Dir', $static).SetValue($null, $workDir)
$storeType.GetField('ImgDir', $static).SetValue($null, (Join-Path $workDir 'images'))

try {
    # 1. Verify all Phase 3 MCP tool names are registered
    $toolNames = $apiTools.GetProperty('Names', $static).GetValue($null, $null)
    $expectedPhase3 = @(
        'vault_list', 'vault_extract', 'vault_schema', 'vault_query', 'vault_search',
        'memory_manage', 'session_search', 'skill_manage', 'tool_list', 'tool_create', 'tool_delete',
        'todo_plan', 'chat_steps', 'chat_compact',
        'cron_manage', 'heartbeat_status', 'watch_folder',
        'dashboard_publish', 'canvas_update', 'dashboard_list'
    )
    foreach ($tn in $expectedPhase3) {
        Assert ($toolNames.Contains($tn)) ('ApiTools.Names missing Phase 3 tool: ' + $tn)
    }

    # 2. Auto-seeding SOUL/USER/TOOLS/MEMORY/GOALS/HEARTBEAT.md + Visual-First Canvas directive
    Invoke-S $memType 'EnsureSeeded' @() | Out-Null
    $memDir = [string]$memType.GetProperty('MemoryDir', $static).GetValue($null, $null)
    foreach ($mf in @('SOUL.md', 'USER.md', 'TOOLS.md', 'MEMORY.md', 'GOALS.md', 'HEARTBEAT.md')) {
        $fp = Join-Path $memDir $mf
        Assert ([IO.File]::Exists($fp)) ('Missing seeded memory file: ' + $mf)
    }
    $soulText = [IO.File]::ReadAllText((Join-Path $memDir 'SOUL.md'), $utf8)
    Assert ($soulText.Contains('canvas_update') -and $soulText.Contains('anti-slop-web-design') -and $soulText.Contains('Obsidian Dark')) 'Seeded SOUL.md missing Visual-First Live Communication Canvas directive.'

    $sysPrompt = [string](Invoke-S $harnessType 'SystemPrompt' @($null, $null))
    Assert ($sysPrompt.Contains('Visual-First Live Communication Canvas') -and $sysPrompt.Contains('canvas_update') -and $sysPrompt.Contains('vault_query')) 'SystemPrompt missing Visual-First Canvas or Phase 3 tools.'

    # 3. Daily logs (today + yesterday) + Frozen Snapshot stability + ResetSnapshot + Lean Subagents
    $dailyDir = [string]$memType.GetProperty('DailyDir', $static).GetValue($null, $null)
    $todayDate = [DateTime]::Now.Date.ToString('yyyy-MM-dd')
    $yestDate = [DateTime]::Now.Date.AddDays(-1).ToString('yyyy-MM-dd')
    [IO.File]::WriteAllText((Join-Path $dailyDir ($yestDate + '.md')), "- Yesterday milestone: calibrated line 3`n", $utf8)
    $null = Run-Tool 'memory_manage' @{ file = 'daily'; action = 'daily_log'; content = 'Today milestone: validated vault SQL joins' }

    $chatId = 'hermes-snap-chat'
    $snapFull1 = [string](Invoke-S $memType 'GetFrozenSnapshot' @($chatId, $false))
    Assert ($snapFull1.Contains('SOUL.md')) 'Full frozen snapshot missing SOUL.md.'
    Assert ($snapFull1.Contains('calibrated line 3')) 'Frozen snapshot missing yesterday daily log.'
    Assert ($snapFull1.Contains('validated vault SQL joins')) 'Frozen snapshot missing today daily log.'

    # Lean Subagents mode MUST omit SOUL.md
    $snapLean = [string](Invoke-S $memType 'GetFrozenSnapshot' @($chatId, $true))
    Assert (-not $snapLean.Contains('### SOUL.md')) 'Lean snapshot unexpectedly included SOUL.md.'
    Assert ($snapLean.Contains('### TOOLS.md')) 'Lean snapshot missing TOOLS.md.'

    $leanSys = [string](Invoke-S $harnessType 'SystemPromptLean' @($null, $null))
    Assert (-not $leanSys.Contains('# SOUL') -and $leanSys.Contains('Lean Subagent Worker')) 'SystemPromptLean did not strip SOUL.md.'

    # Mid-session memory_manage(add) persists to disk WITHOUT mutating active chat's frozen snapshot!
    $marker = 'UNIQUE_FACT_ALPHA_9988'
    $null = Run-Tool 'memory_manage' @{ file = 'MEMORY.md'; action = 'add'; content = ('- ' + $marker) }
    $readMem = Run-Tool 'memory_manage' @{ file = 'MEMORY.md'; action = 'read' }
    Assert ([string]$readMem['content'] -like ('*' + $marker + '*')) 'memory_manage(add) did not persist to disk.'

    $snapFull2 = [string](Invoke-S $memType 'GetFrozenSnapshot' @($chatId, $false))
    Assert (-not $snapFull2.Contains($marker)) 'Frozen snapshot mutated mid-session! Prompt cache prefix would break.'

    Invoke-S $memType 'ResetSnapshot' @($chatId) | Out-Null
    $snapFull3 = [string](Invoke-S $memType 'GetFrozenSnapshot' @($chatId, $false))
    Assert ($snapFull3.Contains($marker)) 'ResetSnapshot did not pick up persisted MEMORY.md update.'

    # 4. Tier 2 Project Context (CLAUDE.md/AGENTS.md) + Shared Harness Hook on regular Agents page chat
    $projDir = [IO.Directory]::CreateDirectory((Join-Path $workDir 'sample-repo')).FullName
    [IO.File]::WriteAllText((Join-Path $projDir 'AGENTS.md'), "# Project Rule`nPROJECT_MARKER_TIER2_XYZ`n", $utf8)
    $asstChat = New-Obj 'AgentChat'
    $asstChat.Id = $chatId; $asstChat.Collection = 'Assistant'; $asstChat.Folder = $projDir
    $wrapped3Tier = [string](Invoke-S $harnessType 'WrapMessage' @($asstChat, $null, 'Check status'))
    Assert ($wrapped3Tier.Contains('[TIER 1: STABLE]') -and $wrapped3Tier.Contains('[TIER 2: PROJECT CONTEXT]') -and $wrapped3Tier.Contains('[TIER 3: VOLATILE]')) '3-Tier prompt markers missing.'
    Assert ($wrapped3Tier.Contains('PROJECT_MARKER_TIER2_XYZ')) 'Tier 2 did not auto-read AGENTS.md from active chat folder.'

    $regChat = New-Obj 'AgentChat'
    $regChat.Id = 'regular-claude-1'; $regChat.Collection = ''; $regChat.Folder = $projDir
    $mUser = New-Obj 'AgentMsg'; $mUser.Role = 'user'; $mUser.Text = 'Review my code'; $mUser.Time = [DateTime]::Now.Ticks
    $regChat.Messages.Add($mUser)
    $regPrompt = [string](Invoke-S $agentStore 'PromptFor' @($regChat, 'claude'))
    Assert ($regPrompt.Contains('session_search') -and $regPrompt.Contains('vault_query')) 'Shared Harness Hook missing on regular Agents page claude prompt.'

    # 5. Skills Engine (skill_manage save/list/read/patch/delete + Progressive Disclosure)
    $skillMd = "---`nname: factory-kpi`ndescription: Computes daily line yield and scrap alerts from vault.db`n---`n# Factory KPI`nStep 1: Query vault.`n"
    $saveSk = Run-Tool 'skill_manage' @{ action = 'save'; slug = 'factory-kpi'; content = $skillMd }
    Assert ([bool]$saveSk['saved']) 'skill_manage save failed.'

    $listSk = Run-Tool 'skill_manage' @{ action = 'list' }
    $slugs = @($listSk['skills'] | ForEach-Object { [string]$_['slug'] })
    Assert ($slugs -contains 'factory-kpi') 'skill_manage list missing saved factory-kpi skill.'
    Assert ($slugs -contains 'anti-slop-web-design') 'Progressive disclosure did not discover workspace anti-slop-web-design skill.'

    $readSk = Run-Tool 'skill_manage' @{ action = 'read'; slug = 'factory-kpi' }
    Assert ([string]$readSk['content'] -like '*Step 1: Query vault.*') 'skill_manage read returned wrong content.'

    $patchSk = Run-Tool 'skill_manage' @{ action = 'patch'; slug = 'factory-kpi'; old_text = 'Step 1: Query vault.'; content = 'Step 1: Query vault_query JOIN.' }
    Assert ([bool]$patchSk['patched']) 'skill_manage patch failed.'
    $readSk2 = Run-Tool 'skill_manage' @{ action = 'read'; slug = 'factory-kpi' }
    Assert ([string]$readSk2['content'] -like '*Step 1: Query vault_query JOIN.*') 'skill_manage patch did not update file.'

    $delSk = Run-Tool 'skill_manage' @{ action = 'delete'; slug = 'factory-kpi' }
    Assert ([bool]$delSk['deleted']) 'skill_manage delete failed.'

    # 6. DeepSeek Dynamic Tool Registry & Creator Mode (sql + powershell hot-loaded into ApiTools!)
    $invCsv = Join-Path $workDir 'inventory.csv'
    [IO.File]::WriteAllText($invCsv, "sku,qty`r`nA1,3`r`nB2,25`r`nC3,7`r`n", $utf8)
    $extInv = Invoke-S $vaultType 'ExtractAndStore' @($invCsv, $false, 25000)
    $invTable = [string]$extInv['tables'][0]['sql_table']

    $createSqlTool = Run-Tool 'tool_create' @{
        name = 'low_stock_check'
        title = 'Low Stock Check'
        description = 'Return SKUs below a given quantity threshold'
        kind = 'sql'
        script = ('SELECT sku, qty FROM "' + $invTable + '" WHERE qty < {{threshold}} ORDER BY sku;')
        args_schema = @{ threshold = @{ type = 'integer'; description = 'Max qty threshold' } }
    }
    Assert ([bool]$createSqlTool['created']) 'tool_create (sql) failed.'

    # Must IMMEDIATELY appear in ApiTools.Schema() and ApiTools.Names without rebuild!
    $dynNames = $apiTools.GetProperty('Names', $static).GetValue($null, $null)
    Assert ($dynNames.Contains('low_stock_check')) 'Newly created dynamic tool not present in ApiTools.Names.'
    $dynSchema = Invoke-S $apiTools 'Schema' @()
    $foundSchema = $dynSchema | Where-Object { $_['name'] -eq 'low_stock_check' } | Select-Object -First 1
    Assert ($null -ne $foundSchema) 'Dynamic tool missing from ApiTools.Schema().'

    # Execute dynamic SQL tool directly through ApiTools.Run
    $runSqlTool = Run-Tool 'low_stock_check' @{ threshold = 10 }
    Assert ([bool]$runSqlTool['ok'] -and [int]$runSqlTool['row_count'] -eq 2) ('Dynamic SQL tool expected 2 rows, got: ' + $runSqlTool['row_count'])
    Assert ([string]$runSqlTool['rows'][0][0] -eq 'A1' -and [string]$runSqlTool['rows'][1][0] -eq 'C3') 'Dynamic SQL tool rows mismatch.'

    # Execute dynamic PowerShell tool receiving AIO_TOOL_ARGS JSON
    $createPsTool = Run-Tool 'tool_create' @{
        name = 'echo_arg_tool'
        title = 'Echo Arg Tool'
        description = 'Reads AIO_TOOL_ARGS'
        kind = 'powershell'
        script = '$a = $env:AIO_TOOL_ARGS | ConvertFrom-Json; Write-Output ("ECHO:" + $a.tag)'
    }
    Assert ([bool]$createPsTool['created']) 'tool_create (powershell) failed.'
    $runPsTool = Run-Tool 'echo_arg_tool' @{ tag = 'FACTORY_42' }
    Assert ([bool]$runPsTool['ok'] -and [string]$runPsTool['output'] -eq 'ECHO:FACTORY_42') ('Dynamic PowerShell tool output mismatch: ' + $runPsTool['output'])

    $delTool1 = Run-Tool 'tool_delete' @{ name = 'low_stock_check' }
    $delTool2 = Run-Tool 'tool_delete' @{ name = 'echo_arg_tool' }
    Assert ([bool]$delTool1['deleted'] -and [bool]$delTool2['deleted']) 'tool_delete failed.'
    $dynNamesAfter = $apiTools.GetProperty('Names', $static).GetValue($null, $null)
    Assert (-not $dynNamesAfter.Contains('low_stock_check')) 'Deleted dynamic tool still present in ApiTools.Names.'

    # 7. Internal Todo Plan + Smart Compaction + Auto Fallback
    $todoSet = Run-Tool 'todo_plan' @{ action = 'set'; chat_id = $chatId; steps = @('Extract workbooks', 'Run SQL JOIN', 'Publish Canvas') }
    Assert ([int]$todoSet['total'] -eq 3 -and [int]$todoSet['done'] -eq 0) 'todo_plan set failed.'
    $todoUpd = Run-Tool 'todo_plan' @{ action = 'update'; chat_id = $chatId; step = 1; status = 'done' }
    Assert ([int]$todoUpd['done'] -eq 1) 'todo_plan update failed.'

    # Smart Context Compaction protecting last 6 turns + flushing daily log + recording step_events checkpoint
    $longChat = New-Obj 'AgentChat'
    $longChat.Id = 'compact-chat-1'; $longChat.Collection = 'Assistant'; $longChat.Title = 'Long Analysis'
    for ($i = 1; $i -le 12; $i++) {
        $mu = New-Obj 'AgentMsg'; $mu.Role = 'user'; $mu.Text = ('Turn ' + $i + ' user question'); $mu.Time = [DateTime]::Now.Ticks + $i
        $ma = New-Obj 'AgentMsg'; $ma.Role = 'agent'; $ma.Agent = 'claude'; $ma.Text = ('Turn ' + $i + ' agent answer'); $ma.Time = [DateTime]::Now.Ticks + $i + 1
        $longChat.Messages.Add($mu)
        $longChat.Messages.Add($ma)
    }
    $chatsList = $agentStore.GetField('Chats', $static).GetValue($null)
    $chatsList.Add($longChat)
    $compRes = Run-Tool 'chat_compact' @{ chat_id = 'compact-chat-1'; protect_last_n = 6 }
    Assert ([bool]$compRes['compacted']) 'chat_compact did not compact 24-message chat.'
    Assert ([int]$compRes['kept_messages'] -eq 7) ('Expected 1 checkpoint + 6 protected messages = 7, got ' + $compRes['kept_messages'])
    Assert ([long]$compRes['checkpoint_step_id'] -gt 0) 'chat_compact did not record checkpoint in step_events.'
    $chatsList.Remove($longChat) | Out-Null

    # Auto Account/Model Fallback checks
    Assert ([bool](Invoke-S $fallbackType 'IsRetryableError' @('HTTP 429 rate limit exceeded'))) 'AgentFallback failed to recognize 429 rate limit.'
    Assert (-not [bool](Invoke-S $fallbackType 'IsRetryableError' @('Syntax error in user file'))) 'AgentFallback misclassified syntax error as retryable.'
    $testDef = New-Obj 'AgentDef'; $testDef.Id = 'claude'; $testDef.ModelArg = '--model {model}'
    $testDef.Models.Add('opus'); $testDef.Models.Add('sonnet')
    $pickArgs = [object[]]@($testDef.PSObject.BaseObject, $null, '', 'opus', [string]$null, [string]$null)
    $picked = [bool]$fallbackType.GetMethod('PickAlternate', $static).Invoke($null, $pickArgs)
    Assert ($picked -and ($null -ne $pickArgs[4] -or $pickArgs[5] -eq 'sonnet')) 'AgentFallback.PickAlternate did not select alternate account or fallback model.'

    # 8. Proactive CronEngine (create, list, 0-token pre-check, [SILENT], HEARTBEAT_OK, FileSystemWatcher)
    foreach ($invalidSchedule in 'daily:25:80', 'every:99999999999999999999999d', 'every:0m') {
        $rejected = $false
        try { $null = Run-Tool 'cron_manage' @{ action = 'create'; schedule = $invalidSchedule } } catch { $rejected = $true }
        Assert $rejected ('Invalid schedule was accepted: ' + $invalidSchedule)
    }
    $watchDir = [IO.Directory]::CreateDirectory((Join-Path $workDir 'watched-reports')).FullName
    $cronCreate = Run-Tool 'cron_manage' @{
        action = 'create'
        id = 'job-watch-1'
        name = 'Watch Reports Folder'
        schedule = 'every:15m'
        precheck_type = 'folder_changed'
        precheck_arg = $watchDir
    }
    Assert ([bool]$cronCreate['created']) 'cron_manage create failed.'

    # Empty folder -> 0-token pre-check MUST return [SILENT]
    $runEmpty = Run-Tool 'cron_manage' @{ action = 'run_now'; id = 'job-watch-1' }
    Assert (-not [bool]$runEmpty['triggered'] -and [string]$runEmpty['status'] -eq '[SILENT]' -and [int]$runEmpty['tokens_used'] -eq 0) ('Expected [SILENT] on empty folder, got: ' + $runEmpty['status'])

    # Drop a file -> pre-check triggers once, then immediate second run returns [SILENT] (unchanged + dedup!)
    [IO.File]::WriteAllText((Join-Path $watchDir 'daily_shift.csv'), "line,output`r`nL1,500`r`n", $utf8)
    $runChanged = Run-Tool 'cron_manage' @{ action = 'run_now'; id = 'job-watch-1' }
    Assert ([bool]$runChanged['triggered'] -and [string]$runChanged['status'] -eq 'TRIGGERED') ('Expected TRIGGERED after file drop, got: ' + $runChanged['status'])

    $runRepeat = Run-Tool 'cron_manage' @{ action = 'run_now'; id = 'job-watch-1' }
    Assert (-not [bool]$runRepeat['triggered'] -and [string]$runRepeat['status'] -eq '[SILENT]') ('Expected [SILENT] on unchanged repeat run, got: ' + $runRepeat['status'])

    # SQL nonzero pre-check returning zero -> [SILENT]
    $null = Run-Tool 'cron_manage' @{
        action = 'create'
        id = 'job-sql-zero'
        name = 'Zero Alert Check'
        schedule = 'daily:08:00'
        precheck_type = 'sql_nonzero'
        precheck_arg = ('SELECT COUNT(*) FROM "' + $invTable + '" WHERE qty < 0;')
    }
    $runSqlZero = Run-Tool 'cron_manage' @{ action = 'run_now'; id = 'job-sql-zero' }
    Assert (-not [bool]$runSqlZero['triggered'] -and [string]$runSqlZero['status'] -eq '[SILENT]') 'sql_nonzero returning 0 did not suppress with [SILENT].'

    # Idle-only Heartbeat check (run twice: second run is guaranteed HEARTBEAT_OK even if host drive > 90% because of alert_dedup!)
    $hb1 = Run-Tool 'heartbeat_status' @{ run_now = $true }
    $hb2 = Run-Tool 'heartbeat_status' @{ run_now = $true }
    Assert ([string]$hb2['status'] -eq 'HEARTBEAT_OK' -and [int]$hb2['tokens_used'] -eq 0) ('Expected HEARTBEAT_OK on clean/deduped check, got: ' + $hb2['status'])

    # FileSystemWatcher auto-indexes dropped CSV after 1.5 s debounce
    $wRes = Run-Tool 'watch_folder' @{ action = 'add'; path = $watchDir }
    Assert ([bool]$wRes['watching']) 'watch_folder add failed.'
    [IO.File]::WriteAllText((Join-Path $watchDir 'auto_drop.csv'), "machine,temp`r`nM9,72.4`r`n", $utf8)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $autoIndexed = $false
    while ($sw.Elapsed.TotalSeconds -lt 10) {
        $docs = Invoke-S $vaultType 'ListDocs' @('auto_drop.csv', 5)
        if ($docs.Count -ge 1) { $autoIndexed = $true; break }
        Start-Sleep -Milliseconds 150
    }
    Assert $autoIndexed 'FileSystemWatcher did not auto-index auto_drop.csv into vault.db.'

    # 9. DashboardGen (dashboard_publish + canvas_update + dashboard_list)
    $pub = Run-Tool 'dashboard_publish' @{
        slug = 'Q4 Factory Comparison'
        title = 'Q4 Factory Comparison'
        html = '<section><h1>Q4 Yield</h1><svg width="200" height="40"><rect width="160" height="24" fill="#d97745"/></svg></section>'
        open = $false
    }
    Assert ([string]$pub['slug'] -eq 'q4-factory-comparison') ('Unexpected sanitized slug: ' + $pub['slug'])
    Assert ([IO.File]::Exists([string]$pub['path'])) 'Published dashboard file not written.'
    $pubHtml = [IO.File]::ReadAllText([string]$pub['path'], $utf8)
    Assert ($pubHtml.Contains('<!doctype html>') -and $pubHtml.Contains('Q4 Yield')) 'Dashboard HTML fragment was not wrapped cleanly.'

    $cv1 = Run-Tool 'canvas_update' @{ title = 'Architecture Live'; html = '<h1>Canvas V1</h1>'; open = $false }
    $cv2 = Run-Tool 'canvas_update' @{ title = 'Architecture Live V2'; html = '<h1>Canvas V2</h1>'; open = $false }
    Assert ([string]$cv1['path'] -eq [string]$cv2['path'] -and [string]$cv2['slug'] -eq 'live-canvas') 'canvas_update did not target live-canvas.html.'
    Assert (([IO.File]::ReadAllText([string]$cv2['path'], $utf8)).Contains('Canvas V2')) 'canvas_update did not overwrite live-canvas.html.'

    $dashList = Run-Tool 'dashboard_list' @{}
    Assert ($dashList.Count -eq 2) ('Expected 2 dashboards in dashboard_list, got ' + $dashList.Count)
} finally {
    Invoke-S $cronType 'Stop' @() | Out-Null
    Invoke-S $vaultType 'Close' @() | Out-Null
    Start-Sleep -Milliseconds 100
    [IO.Directory]::Delete($workDir, $true)
    Assert (-not [IO.Directory]::Exists($workDir)) 'Temp hermes-memory directory was not cleanly deleted.'
}

Write-Host 'HERMES MEMORY OK: seeded memory + Visual Canvas SOUL + Frozen Snapshot + daily logs + Lean Subagents + 3-Tier Prompt + Skills + Dynamic Tools (SQL+PS) + Todo + Compaction + Fallback + Cron/[SILENT]/HEARTBEAT_OK/Watcher + Live Canvas'
