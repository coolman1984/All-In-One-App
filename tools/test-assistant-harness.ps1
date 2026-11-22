# Phase 1 verification for the All in One Autonomous Assistant Companion harness.
# Runs offscreen with isolated fixture data; never touches real user data or desktop windows.
param([string]$AssemblyPath = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)

$root = Split-Path -Parent $PSScriptRoot
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $root 'build-test\StickyNotesPro.test.exe' }
$asm = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($AssemblyPath))
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$asm.GetType('StickyPro.WebViewHost', $true).GetMethod('Hook', $static).Invoke($null, @()) | Out-Null

function Assert($ok, [string]$msg) { if (-not $ok) { throw $msg } }
function New-Obj([string]$typeName) { [Activator]::CreateInstance($asm.GetType('StickyPro.' + $typeName, $true), $true) }
function Unwrap($v) { if ($null -eq $v) { return $null }; return $v.PSObject.BaseObject }
function Invoke-S($type, [string]$method, [object[]]$arguments) {
    $raw = New-Object object[] $arguments.Count
    for ($i = 0; $i -lt $arguments.Count; $i++) { $raw[$i] = Unwrap $arguments[$i] }
    return $type.GetMethod($method, $static).Invoke($null, $raw)
}

$openBracket = [string][char]0x27E6
$closeBracket = [string][char]0x27E7

$fixture = [IO.Directory]::CreateDirectory((Join-Path $root ('build-test\assistant-harness-' + [guid]::NewGuid().ToString('N')))).FullName
$storeType = $asm.GetType('StickyPro.Store', $true)
$storeType.GetField('Dir', $static).SetValue($null, $fixture)
$storeType.GetField('ImgDir', $static).SetValue($null, (Join-Path $fixture 'images'))

$harness = $asm.GetType('StickyPro.AssistantHarness', $true)
$agentStore = $asm.GetType('StickyPro.AgentStore', $true)
$chatView = $asm.GetType('StickyPro.ChatView', $true)
$apiTools = $asm.GetType('StickyPro.ApiTools', $true)

try {
    # 1. WrapMessage, WrapTeamPrompt, ChatView.ShownText, ApplyMcpArgs
    $soloChat = New-Obj 'AgentChat'
    $soloChat.Id = 'wrap-test'
    $soloChat.Collection = 'Assistant'
    $soloChat.Mode = 'auto'

    $ctx = New-Object 'Collections.Generic.Dictionary[string,object]'
    $ctx['page'] = 'documents'
    $ctx['documents_current'] = 'C:\Docs\Q4' + $closeBracket + 'Report.xlsx'
    $tabsList = New-Object 'Collections.Generic.List[string]'
    $tabsList.Add('C:\Docs\Q4Report.xlsx')
    $ctx['documents_tabs'] = $tabsList

    $wrapped = [string](Invoke-S $harness 'WrapMessage' @($soloChat, $ctx, 'Update cell B2 now'))
    Assert ($wrapped.StartsWith($openBracket)) 'WrapMessage did not start with hidden open bracket.'
    Assert ($wrapped.Contains($closeBracket)) 'WrapMessage did not contain hidden close bracket.'
    Assert ($wrapped.Contains('autonomous desktop companion')) 'WrapMessage missing standalone companion identity.'
    Assert ($wrapped.Contains('NEVER ask for permission')) 'WrapMessage missing open-automation directive.'
    Assert ($wrapped.Contains('documents_list') -and $wrapped.Contains('document_switch') -and $wrapped.Contains('document_save')) 'WrapMessage missing document MCP tool summary.'
    Assert ($wrapped.Contains('Q4Report.xlsx')) 'WrapMessage did not serialize list context cleanly.'
    Assert (-not $wrapped.Contains('System.Collections.Generic.List')) 'WrapMessage leaked raw .NET collection type name.'

    $shown = [string](Invoke-S $chatView 'ShownText' @($wrapped))
    Assert ($shown -eq 'Update cell B2 now') ('ChatView.ShownText did not strip hidden header cleanly: ' + $shown)

    # Mode-specific prompts (Council and Leader Team)
    $soloChat.Council = $true
    $soloChat.LeaderTeam = $false
    $councilPrompt = [string](Invoke-S $harness 'SystemPrompt' @($soloChat, $ctx))
    Assert ($councilPrompt.Contains('Model Council')) 'SystemPrompt missing Council mode guidance.'

    $soloChat.LeaderTeam = $true
    $leaderPrompt = [string](Invoke-S $harness 'SystemPrompt' @($soloChat, $ctx))
    Assert ($leaderPrompt.Contains('Leader Team')) 'SystemPrompt missing Leader Team guidance.'

    # WrapTeamPrompt wraps Assistant chats and skips regular chats
    $teamWrapped = [string](Invoke-S $harness 'WrapTeamPrompt' @($soloChat, 'Worker assignment'))
    Assert ($teamWrapped.StartsWith($openBracket) -and $teamWrapped.Contains('Worker assignment')) 'WrapTeamPrompt did not wrap Assistant team prompt.'
    $regularChat = New-Obj 'AgentChat'
    $regularChat.Id = 'regular-1'
    $regularChat.Collection = ''
    $teamSkipped = [string](Invoke-S $harness 'WrapTeamPrompt' @($regularChat, 'Worker assignment'))
    Assert ($teamSkipped -eq 'Worker assignment') 'WrapTeamPrompt unexpectedly wrapped a non-Assistant chat.'

    # ApplyMcpArgs per CLI + assistant-mcp.json + Dirs()
    $dashDir = [IO.Directory]::CreateDirectory((Join-Path $fixture 'dashboards')).FullName
    $dirs = Invoke-S $harness 'Dirs' @()
    $dirsArr = @($dirs)
    Assert ($dirsArr -contains $fixture -and $dirsArr -contains $dashDir) 'AssistantHarness.Dirs did not include Store.Dir and dashboards.'

    $claudeDef = New-Obj 'AgentDef'; $claudeDef.Id = 'claude'
    $claudeArgs = [string](Invoke-S $harness 'ApplyMcpArgs' @($claudeDef, $soloChat, '-p --output-format stream-json'))
    Assert ($claudeArgs.Contains('--mcp-config') -and $claudeArgs.Contains('--allowedTools mcp__all-in-one') -and $claudeArgs.Contains('--add-dir')) ('Claude MCP args incomplete: ' + $claudeArgs)
    $mcpCfgPath = Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'AllInOne-agents') 'assistant-mcp.json'
    Assert ([IO.File]::Exists($mcpCfgPath)) 'ApplyMcpArgs did not write assistant-mcp.json.'
    $mcpCfgText = [IO.File]::ReadAllText($mcpCfgPath)
    Assert ($mcpCfgText.Contains('"all-in-one"') -and $mcpCfgText.Contains('"--mcp"')) 'assistant-mcp.json missing all-in-one --mcp server config.'

    $codexDef = New-Obj 'AgentDef'; $codexDef.Id = 'codex'
    $codexArgs = [string](Invoke-S $harness 'ApplyMcpArgs' @($codexDef, $soloChat, 'exec --json -'))
    Assert ($codexArgs.Contains('mcp_servers.all_in_one.command=') -and $codexArgs.Contains('mcp_servers.all_in_one.args=') -and $codexArgs.Contains('--add-dir')) ('Codex MCP args incomplete: ' + $codexArgs)

    foreach ($otherId in @('agy', 'cline', 'opencode')) {
        $otherDef = New-Obj 'AgentDef'; $otherDef.Id = $otherId
        $orig = '--json sample'
        $otherArgs = [string](Invoke-S $harness 'ApplyMcpArgs' @($otherDef, $soloChat, $orig))
        Assert ($otherArgs -eq $orig) ($otherId + ' received unexpected MCP CLI flags: ' + $otherArgs)
    }

    # Verify StartMember delivers wrapped Assistant companion prompt to a Council/Leader stand-in worker
    $probeScript = Join-Path $fixture 'probe-worker.ps1'
    @'
$msg = [Console]::In.ReadToEnd()
if ($msg.Contains('autonomous desktop companion') -and $msg.Contains('Current page:') -and $msg.Contains('Probe worker task')) {
    Write-Output 'COMPANION_WRAP_VERIFIED'
} else {
    Write-Output 'MISSING_COMPANION_WRAP'
    exit 1
}
'@ | Set-Content -LiteralPath $probeScript -Encoding UTF8
    $defs = $agentStore.GetProperty('Defs', $static).GetValue($null, $null)
    $probeDef = New-Obj 'AgentDef'
    $probeDef.Id = 'probe-worker'; $probeDef.Name = 'ProbeWorker'; $probeDef.Stdin = $true
    $probeDef.Exe = Join-Path $PSHOME 'powershell.exe'
    $probeDef.Args = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $probeScript + '"'
    $defs.Add($probeDef)

    $teamChat = New-Obj 'AgentChat'
    $teamChat.Id = 'asst-team-probe'; $teamChat.Collection = 'Assistant'; $teamChat.Mode = 'auto'; $teamChat.Folder = $fixture; $teamChat.Council = $true; $teamChat.Rounds = 0
    $m1 = New-Obj 'CouncilMember'; $m1.Agent = 'probe-worker'; $m1.Key = 'pw1'
    $teamChat.Members.Add($m1)
    $teamPane = [Activator]::CreateInstance($asm.GetType('StickyPro.ChatPane', $true), @((Unwrap $teamChat)))
    try {
        $null = $teamPane.Handle
        $teamPane.Send('Probe worker task')
        $tw = [Diagnostics.Stopwatch]::StartNew()
        while ($teamPane.Busy -and $tw.Elapsed.TotalSeconds -lt 25) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
        Assert (-not $teamPane.Busy) 'Assistant team probe did not finish.'
        $gotOk = @($teamChat.Messages | Where-Object { $_.Role -eq 'agent' -and $_.Text.Contains('COMPANION_WRAP_VERIFIED') }).Count
        Assert ($gotOk -ge 1) 'StartMember did not deliver AssistantHarness.WrapTeamPrompt to Assistant team member.'
    } finally {
        $teamPane.Dispose()
        Invoke-S $agentStore 'Delete' @($teamChat) | Out-Null
        Invoke-S $agentStore 'FlushWrites' @() | Out-Null
    }

    # 2. Isolated Assistant storage, legacy migration, and ChatHistory exclusion
    $assistantChatsDir = [string]$harness.GetProperty('ChatsDir', $static).GetValue($null, $null)
    $agentsChatsDir = Join-Path (Join-Path $fixture 'agents') 'chats'
    Assert ($assistantChatsDir -eq (Join-Path (Join-Path $fixture 'assistant') 'chats')) ('Unexpected Assistant ChatsDir: ' + $assistantChatsDir)

    $agentStore.GetField('Loaded', $static).SetValue($null, $true)
    $chatsProp = $agentStore.GetField('Chats', $static).GetValue($null)
    $chatsProp.Clear()

    $asstChat = New-Obj 'AgentChat'
    $asstChat.Id = 'asst-isolated-1'
    $asstChat.Collection = 'Assistant'
    $asstChat.Title = 'Assistant isolated chat'
    $asstChat.Mode = 'auto'
    $asstChat.Folder = $fixture

    $codeChat = New-Obj 'AgentChat'
    $codeChat.Id = 'code-regular-1'
    $codeChat.Collection = 'Work'
    $codeChat.Title = 'Coding chat'
    $codeChat.Mode = 'edit'
    $codeChat.Folder = $fixture

    Invoke-S $agentStore 'Save' @($asstChat) | Out-Null
    Invoke-S $agentStore 'Save' @($codeChat) | Out-Null
    Assert ([bool](Invoke-S $agentStore 'FlushWrites' @())) 'AgentStore.FlushWrites failed.'

    $asstFile = Join-Path $assistantChatsDir 'asst-isolated-1.json'
    $asstWrongFile = Join-Path $agentsChatsDir 'asst-isolated-1.json'
    $codeFile = Join-Path $agentsChatsDir 'code-regular-1.json'
    Assert ([IO.File]::Exists($asstFile)) 'Assistant chat did not save under data\assistant\chats\.'
    Assert (-not [IO.File]::Exists($asstWrongFile)) 'Assistant chat leaked into data\agents\chats\.'
    Assert ([IO.File]::Exists($codeFile)) 'Regular Agents chat did not save under data\agents\chats\.'

    # Plant a legacy Assistant chat in agents\chats and a duplicate id with older title in agents\chats
    [IO.File]::WriteAllText((Join-Path $agentsChatsDir 'legacy-asst.json'), '{"Id":"legacy-asst","Title":"Legacy assistant","Collection":"Assistant","Mode":"auto","Agents":["claude"],"Messages":[]}')
    [IO.File]::WriteAllText((Join-Path $agentsChatsDir 'asst-isolated-1.json'), '{"Id":"asst-isolated-1","Title":"Stale legacy duplicate","Collection":"Assistant","Mode":"auto","Agents":["claude"],"Messages":[]}')

    $chatsProp.Clear()
    $agentStore.GetField('Loaded', $static).SetValue($null, $false)
    $loadedFlag = $false
    $doneAction = [Action]{ $script:loadedFlag = $true }
    $agentStore.GetMethod('LoadAll', $static).Invoke($null, @($doneAction))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not [bool]$agentStore.GetField('Loaded', $static).GetValue($null) -and $sw.Elapsed.TotalSeconds -lt 10) {
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 20
    }
    Assert ([bool]$agentStore.GetField('Loaded', $static).GetValue($null)) 'AgentStore.LoadAll did not complete.'

    $findMethod = $agentStore.GetMethod('Find', $static)
    $reloadedAsst = $findMethod.Invoke($null, @('asst-isolated-1'))
    $reloadedLegacy = $findMethod.Invoke($null, @('legacy-asst'))
    $reloadedCode = $findMethod.Invoke($null, @('code-regular-1'))
    Assert ($null -ne $reloadedAsst -and $reloadedAsst.Title -eq 'Assistant isolated chat') 'Reload did not prefer data\assistant\chats\ over legacy duplicate.'
    Assert ($null -ne $reloadedLegacy -and $reloadedLegacy.Collection -eq 'Assistant') 'Reload did not load legacy Assistant chat from agents\chats\.'
    Assert ($null -ne $reloadedCode -and $reloadedCode.Title -eq 'Coding chat') 'Reload lost regular Agents chat.'

    # Verify ChatHistory (Agents page sidebar) hides Assistant chats and Assistant folder filter
    $history = New-Obj 'ChatHistory'
    try {
        $history.Fill()
        $shownChats = $history.GetType().GetField('shown', $instance).GetValue($history)
        $shownIds = @($shownChats | ForEach-Object { $_.Id })
        Assert ($shownIds -contains 'code-regular-1') 'ChatHistory omitted regular Agents chat.'
        Assert (-not ($shownIds -contains 'asst-isolated-1') -and -not ($shownIds -contains 'legacy-asst')) 'ChatHistory leaked Assistant chat into Agents sidebar.'
        $filterCombo = $history.GetType().GetField('filter', $instance).GetValue($history)
        $filterItems = @($filterCombo.Items | ForEach-Object { [string]$_ })
        Assert (@($filterItems | Where-Object { $_.IndexOf('Assistant', [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -eq 0) 'ChatHistory filter dropdown leaked Assistant collection.'
        Assert (@($filterItems | Where-Object { $_.EndsWith('Work') }).Count -eq 1) 'ChatHistory filter dropdown missing Work collection.'
    } finally {
        $history.Dispose()
    }

    # Verify Delete removes both new and legacy copies cleanly
    Invoke-S $agentStore 'Delete' @($reloadedAsst) | Out-Null
    Assert (-not [IO.File]::Exists($asstFile) -and -not [IO.File]::Exists($asstWrongFile)) 'AgentStore.Delete left Assistant chat files behind.'

    # 3. AssistantPanel offscreen construction, Solo/Council/Leader switching, and live Activity strip
    $main = New-Obj 'MainForm'
    $panel = [Activator]::CreateInstance($asm.GetType('StickyPro.AssistantPanel', $true), @((Unwrap $main)))
    try {
        Assert ($null -ne $panel.Pane) 'AssistantPanel did not initialize ChatPane.'
        Assert ($panel.Pane.Chat.Collection -eq 'Assistant') 'AssistantPanel chat missing Collection = Assistant.'
        Assert ($panel.Pane.Chat.Mode -eq 'auto') 'AssistantPanel chat did not default to auto mode.'
        Assert (-not $panel.Pane.Chat.Council -and -not $panel.Pane.Chat.LeaderTeam) 'AssistantPanel did not start in Solo mode.'

        $btnWorkspace = $panel.GetType().GetField('btnWorkspace', $instance).GetValue($panel)
        Assert (-not $btnWorkspace.Enabled) 'Workspace button should be disabled in Solo mode.'

        $panel.SetMode('council')
        Assert ($panel.Pane.Chat.Council -and -not $panel.Pane.Chat.LeaderTeam) 'SetMode(council) failed.'
        Assert ($panel.Pane.Chat.Members.Count -gt 0) 'SetMode(council) did not seed members.'
        Assert (-not $btnWorkspace.Enabled) 'Workspace button should be disabled in Council mode.'

        $panel.SetMode('leader')
        Assert ($panel.Pane.Chat.Council -and $panel.Pane.Chat.LeaderTeam) 'SetMode(leader) failed.'
        Assert ($btnWorkspace.Enabled) 'Workspace button should be enabled in Leader mode.'

        $panel.SetMode('solo')
        Assert (-not $panel.Pane.Chat.Council -and -not $panel.Pane.Chat.LeaderTeam) 'SetMode(solo) failed.'

        # Activity event updates live status bar and tolerates throwing listeners
        $badListener = [Action[string]]{ param($s) throw 'listener-boom' }
        $activityEvent = $harness.GetEvent('Activity', $static)
        $activityEvent.AddEventHandler($null, $badListener)
        try {
            $harness.GetMethod('Report', $static).Invoke($null, @('Opening Report.xlsx in Documents...'))
            Assert ($panel.StatusText -eq 'Opening Report.xlsx in Documents...') ('Live status bar did not update on Activity: ' + $panel.StatusText)
        } finally {
            $activityEvent.RemoveEventHandler($null, $badListener)
        }

        $panel.PageChanged('tasks')
        Assert ($panel.StatusText.StartsWith('Seeing:')) ('PageChanged did not refresh Seeing strip: ' + $panel.StatusText)
    } finally {
        $panel.Dispose()
    }
    # Firing Report after panel Dispose must be safe
    $harness.GetMethod('Report', $static).Invoke($null, @('Post-dispose check'))

    # 4. Live PageContext() + page view hooks
    try {
        $allPages = $apiTools.GetField('Pages', $static).GetValue($null)
        $expectedPages = @($main.NavPages | ForEach-Object { $_.Key }) + 'shortcuts'
        $pageDifferences = @(Compare-Object $expectedPages $allPages)
        Assert ($pageDifferences.Count -eq 0) 'The local API and desktop navigation disagree about available pages.'
        Assert ($allPages -contains 'team') 'Team is missing from the local API.'
        foreach ($pageKey in $allPages) {
            $main.ShowPage($pageKey)
            $pctx = $main.PageContext()
            Assert ($pctx['page'] -eq $pageKey) ('PageContext page mismatch for ' + $pageKey)
            foreach ($reqKey in @('loaded_pages', 'documents_current', 'documents_tabs', 'documents_editing', 'explorer_folder', 'explorer_selected', 'browser_title', 'browser_url', 'markdown_note')) {
                Assert ($pctx.ContainsKey($reqKey)) ('PageContext missing global key ' + $reqKey + ' on page ' + $pageKey)
            }
        }
        $main.ShowPage('documents')
        $docCtx = $main.PageContext()
        Assert ($docCtx.ContainsKey('documents_mode') -and $docCtx.ContainsKey('documents_kind') -and $docCtx.ContainsKey('documents_tab_count')) 'Documents PageContext missing document details.'
        Assert ($null -ne $main.DocumentsView) 'MainForm.DocumentsView is null after visiting documents.'
        Assert ($null -ne $main.ExplorerView) 'MainForm.ExplorerView is null after visiting explorer.'
        Assert ($null -ne $main.BrowserView) 'MainForm.BrowserView is null after visiting browser.'

        # DocumentsPage helper checks
        $dv = $main.DocumentsView
        Assert ($dv.OpenTabs.Count -eq 0) 'Fresh DocumentsView should have 0 tabs.'
        Assert (-not $dv.IsEditing('C:\none.xlsx')) 'IsEditing should be false for unopened file.'
        Assert (-not $dv.SwitchTo('C:\definitely-missing-file.xlsx')) 'SwitchTo should return false for missing file.'
        $saveArgs = [object[]]@('', [string]$null)
        $savedOk = [bool]$dv.GetType().GetMethod('SaveDocument').Invoke($dv, $saveArgs)
        Assert (-not $savedOk -and -not [string]::IsNullOrEmpty($saveArgs[1])) 'SaveDocument should fail cleanly with error when no document is active.'

        # ExplorerPage and BrowserPage helpers
        Assert ($main.ExplorerView.SelectedPaths().Count -eq 0) 'Fresh ExplorerView SelectedPaths should be empty.'
        Assert ($null -ne $main.ExplorerView.PaneFolders()) 'ExplorerView.PaneFolders returned null.'
        Assert ($null -ne $main.BrowserView.OpenTabsInfo()) 'BrowserView.OpenTabsInfo returned null.'
    } finally {
        $main.Dispose()
    }

    # 5. ApiTools schema verification
    $toolNames = $apiTools.GetProperty('Names', $static).GetValue($null, $null)
    foreach ($tn in @('document_open', 'documents_list', 'document_switch', 'document_save')) {
        Assert ($toolNames.Contains($tn)) ('ApiTools.Names missing ' + $tn)
    }
    $schema = $apiTools.GetMethod('Schema', $static).Invoke($null, @())
    $docOpenSchema = $schema | Where-Object { $_['name'] -eq 'document_open' } | Select-Object -First 1
    Assert ($null -ne $docOpenSchema) 'document_open schema entry not found.'
    $props = $docOpenSchema['inputSchema']['properties']
    Assert ($props.ContainsKey('edit') -and $props['edit']['type'] -eq 'boolean') 'document_open schema missing boolean edit parameter.'
} finally {
    try { [IO.Directory]::Delete($fixture, $true) } catch { }
}

Write-Host 'ASSISTANT HARNESS OK: wrapping, MCP CLI wiring, isolated storage + legacy reload, ChatHistory separation, Solo/Council/Leader panel, PageContext, document MCP schema'
