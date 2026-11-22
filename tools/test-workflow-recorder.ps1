# Phase 4 verification for WorkflowRecorder (Show-Me-Once semantic UIA + smart keyframe JPEGs),
# 2 New Studio App Designs (Atelier & Nordic, Light + Dark), and 16 Zero-Exit PC MCP Tools.
param([string]$AssemblyPath = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)

$root = Split-Path -Parent $PSScriptRoot
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $root 'build-test\StickyNotesPro.test.exe' }
$asm = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($AssemblyPath))
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
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
function Hex-Of([Drawing.Color]$c) {
    return ('#{0:X2}{1:X2}{2:X2}' -f $c.R, $c.G, $c.B)
}

$workDir = [IO.Directory]::CreateDirectory((Join-Path $root ('build-test\phase4-test-' + [guid]::NewGuid().ToString('N')))).FullName
$storeType = $asm.GetType('StickyPro.Store', $true)
$vaultType = $asm.GetType('StickyPro.SqliteVault', $true)
$recType = $asm.GetType('StickyPro.WorkflowRecorder', $true)
$uiaType = $asm.GetType('StickyPro.UiaEngine', $true)
$subType = $asm.GetType('StickyPro.SubagentRunner', $true)
$uiType = $asm.GetType('StickyPro.Ui', $true)
$themeType = $asm.GetType('StickyPro.Theme', $true)
$apiTools = $asm.GetType('StickyPro.ApiTools', $true)
$js = New-Object Web.Script.Serialization.JavaScriptSerializer
$utf8 = New-Object Text.UTF8Encoding($false)

$storeType.GetField('DirOverride', $static).SetValue($null, $workDir)
Invoke-S $storeType 'Load' @() | Out-Null

try {
    # 1. Verify all 20 Phase 4 MCP tools are registered in ApiTools.Names
    $toolNames = $apiTools.GetProperty('Names', $static).GetValue($null, $null)
    $expectedPhase4 = @(
        'record_start', 'record_stop', 'record_status', 'record_list',
        'explorer_list', 'explorer_action',
        'system_processes', 'system_drives', 'process_kill',
        'browser_cdp', 'chrome_bookmarks',
        'tagged_list', 'tagged_add', 'activity_recent',
        'snip_ocr', 'app_screenshot',
        'uia_inspect', 'uia_action',
        'sessions_spawn', 'sessions_status'
    )
    foreach ($tn in $expectedPhase4) {
        Assert ($toolNames.Contains($tn)) ('ApiTools.Names missing Phase 4 tool: ' + $tn)
    }

    # 2. Verify WorkflowRecorder lifecycle, keyframes, events.jsonl, summary.md, and MCP dispatch
    $st0 = Run-Tool 'record_status' @{}
    Assert (-not [bool]$st0['recording']) 'WorkflowRecorder should be idle initially.'

    $startRes = Run-Tool 'record_start' @{
        title = 'Monthly Factory Yield Reconciliation'
        notes = 'Open Excel sheet, filter scrap > 2%, publish Live Canvas'
    }
    Assert ([bool]$startRes['ok'] -and [bool]$startRes['recording']) 'record_start failed.'
    $recId = [string]$startRes['id']
    $recFolder = [string]$startRes['folder']
    Assert ($recId.StartsWith('rec-') -and [IO.Directory]::Exists($recFolder)) ('Invalid recording folder: ' + $recFolder)

    # Simulate steps (click, key, app_action)
    $step1 = Invoke-S $recType 'RecordClick' @(240, 180, 'left')
    Assert ([int]$step1['step'] -eq 1 -and [string]$step1['kind'] -eq 'click') 'Step 1 RecordClick failed.'
    $frame1Rel = [string]$step1['frame']
    Assert ($frame1Rel -eq 'frames/step-001.jpg') ('Unexpected frame 1 relative path: ' + $frame1Rel)
    $frame1Full = Join-Path $recFolder ($frame1Rel -replace '/', '\')
    Assert ([IO.File]::Exists($frame1Full)) 'Keyframe JPEG step-001.jpg was not written to disk.'
    $fi1 = New-Object IO.FileInfo($frame1Full)
    Assert ($fi1.Length -gt 1024 -and $fi1.Length -lt 400000) ('Keyframe JPEG size out of expected range: ' + $fi1.Length)
    $bmpCheck = [Drawing.Image]::FromFile($frame1Full)
    try {
        Assert ($bmpCheck.Width -le 1280 -and $bmpCheck.Width -ge 320) ('Keyframe width out of bounds: ' + $bmpCheck.Width)
    } finally { $bmpCheck.Dispose() }

    $step2 = Invoke-S $recType 'RecordKey' @('Ctrl+S')
    Assert ([int]$step2['step'] -eq 2 -and [string]$step2['kind'] -eq 'key' -and [string]$step2['keys'] -eq 'Ctrl+S') 'Step 2 RecordKey failed.'

    $step3 = Invoke-S $recType 'RecordAppAction' @('documents', 'Saved Q4_Yield.xlsx [Summary!B12]')
    Assert ([int]$step3['step'] -eq 3 -and [string]$step3['kind'] -eq 'app_action') 'Step 3 RecordAppAction failed.'

    $midStatus = Run-Tool 'record_status' @{}
    Assert ([bool]$midStatus['recording'] -and [int]$midStatus['stepCount'] -eq 3) ('record_status expected 3 steps, got: ' + $midStatus['stepCount'])

    # A torn append is replaced from the in-memory steps at stop.
    [IO.File]::WriteAllText([string]$startRes['eventsFile'], '{partial', $utf8)
    $stopRes = Run-Tool 'record_stop' @{}
    Assert ([bool]$stopRes['ok'] -and (-not [bool]$stopRes['recording']) -and [int]$stopRes['stepCount'] -eq 3) 'record_stop failed.'
    $summaryFile = [string]$stopRes['summaryFile']
    $eventsFile = [string]$stopRes['eventsFile']
    Assert ([IO.File]::Exists($summaryFile) -and [IO.File]::Exists($eventsFile)) 'summary.md or events.jsonl missing after record_stop.'
    $summaryText = [IO.File]::ReadAllText($summaryFile, $utf8)
    Assert ($summaryText.Contains('Monthly Factory Yield Reconciliation') -and $summaryText.Contains('workflow-to-automation') -and $summaryText.Contains('tool_create')) 'summary.md missing Show-Me-Once blueprint.'
    $eventLines = @([IO.File]::ReadAllLines($eventsFile, $utf8) | Where-Object { $_.Trim().Length -gt 0 })
    Assert ($eventLines.Count -eq 3) ('events.jsonl expected 3 lines, got: ' + $eventLines.Count)

    $listRes = Run-Tool 'record_list' @{ limit = 10 }
    Assert ([bool]$listRes['ok'] -and [int]$listRes['count'] -ge 1) 'record_list failed.'
    $getRes = Run-Tool 'record_list' @{ id = $recId }
    Assert ([bool]$getRes['ok'] -and [int]$getRes['stepCount'] -eq 3 -and @($getRes['events']).Count -eq 3) 'record_list(id) failed.'

    # 3. Verify explorer_action & explorer_list
    $expSandbox = Join-Path $workDir 'explorer-sandbox'
    $subA = Join-Path $expSandbox 'line-a'
    $mkRes = Run-Tool 'explorer_action' @{ action = 'mkdir'; path = $subA }
    Assert ([bool]$mkRes['ok'] -and [IO.Directory]::Exists($subA)) 'explorer_action(mkdir) failed.'

    $srcFile = Join-Path $subA 'report-draft.txt'
    [IO.File]::WriteAllText($srcFile, 'Line A yield 98.4%', $utf8)
    $copyFile = Join-Path $subA 'report-copy.txt'
    $cpRes = Run-Tool 'explorer_action' @{ action = 'copy'; path = $srcFile; dest = $copyFile }
    Assert ([bool]$cpRes['ok'] -and [IO.File]::Exists($copyFile)) 'explorer_action(copy) failed.'

    $renRes = Run-Tool 'explorer_action' @{ action = 'rename'; path = $copyFile; dest = 'report-final.txt' }
    $finalFile = Join-Path $subA 'report-final.txt'
    Assert ([bool]$renRes['ok'] -and [IO.File]::Exists($finalFile) -and (-not [IO.File]::Exists($copyFile))) 'explorer_action(rename) failed.'

    $listExp = Run-Tool 'explorer_list' @{ path = $subA; filter = '*.txt' }
    Assert ([bool]$listExp['ok'] -and [int]$listExp['count'] -eq 2) ('explorer_list expected 2 txt files, got: ' + $listExp['count'])

    $delRes = Run-Tool 'explorer_action' @{ action = 'delete_recycle'; path = $finalFile }
    Assert ([bool]$delRes['ok'] -and (-not [IO.File]::Exists($finalFile))) 'explorer_action(delete_recycle) failed.'

    # 4. Verify system_processes, system_drives, process_kill safety guard
    $procRes = Run-Tool 'system_processes' @{ sortBy = 'ram'; limit = 15 }
    Assert ([bool]$procRes['ok'] -and [int]$procRes['count'] -gt 0) 'system_processes returned 0 processes.'

    $drvRes = Run-Tool 'system_drives' @{}
    Assert ([bool]$drvRes['ok'] -and [int]$drvRes['count'] -ge 1) 'system_drives returned 0 drives.'
    $cDrive = @($drvRes['drives'] | Where-Object { $_['letter'] -eq 'C' })
    Assert ($cDrive.Count -eq 1 -and [double]$cDrive[0]['totalGB'] -gt 1) 'system_drives missing C: drive metrics.'

    $selfPid = [Diagnostics.Process]::GetCurrentProcess().Id
    $threwSelfKill = $false
    try { $null = Run-Tool 'process_kill' @{ pid = $selfPid } } catch { $threwSelfKill = $true }
    Assert $threwSelfKill 'process_kill must refuse to kill the current AllInOne process.'

    # 5. Verify tagged_add, tagged_list, activity_recent
    $tagRes = Run-Tool 'tagged_add' @{ path = $srcFile; tag = 'Urgent'; note = 'Check before 9am shift' }
    Assert ([bool]$tagRes['ok'] -and [string]$tagRes['category'] -eq 'Urgent') 'tagged_add failed.'

    $tagList = Run-Tool 'tagged_list' @{ tag = 'Urgent'; query = 'shift' }
    Assert ([bool]$tagList['ok'] -and [int]$tagList['count'] -eq 1) 'tagged_list failed to find tagged file.'

    $actList = $storeType.GetField('Activity', $static).GetValue($null)
    $actItem = New-Obj 'ActivityRecord'
    $actItem.Id = 'act-test-1'; $actItem.Process = 'Excel'; $actItem.Path = 'C:\Reports\Yield_Q4.xlsx'; $actItem.Event = 'Opened'; $actItem.Time = [DateTime]::Now.Ticks; $actItem.Size = 40960
    $actList.Insert(0, $actItem)
    $actRes = Run-Tool 'activity_recent' @{ kind = 'Excel'; limit = 10 }
    Assert ([bool]$actRes['ok'] -and [int]$actRes['count'] -ge 1) 'activity_recent failed to return Excel record.'

    # 6. Verify app_screenshot
    $shotRes = Run-Tool 'app_screenshot' @{ target = 'app' }
    Assert ([bool]$shotRes['ok'] -and [IO.File]::Exists([string]$shotRes['path']) -and [int]$shotRes['width'] -ge 320 -and [int]$shotRes['bytes'] -gt 500) 'app_screenshot failed.'

    # 7. Verify uia_inspect and uia_action against a live WinForms test dialog
    $testForm = New-Object Windows.Forms.Form
    $testForm.Text = 'UIA_TEST_TARGET_WINDOW'
    $testForm.Size = New-Object Drawing.Size(360, 200)
    $testForm.StartPosition = 'Manual'
    $testForm.Location = New-Object Drawing.Point(120, 120)
    $testBox = New-Object Windows.Forms.TextBox
    $testBox.Name = 'txtOrderCode'
    $testBox.Text = 'INITIAL_VAL'
    $testBox.Location = New-Object Drawing.Point(20, 20)
    $testBox.Width = 200
    $script:btnClicked = $false
    $testBtn = New-Object Windows.Forms.Button
    $testBtn.Name = 'btnSubmitOrder'
    $testBtn.Text = 'Submit Order'
    $testBtn.Location = New-Object Drawing.Point(20, 65)
    $testBtn.Add_Click({ $script:btnClicked = $true })
    $testForm.Controls.Add($testBox)
    $testForm.Controls.Add($testBtn)
    $testForm.Show()
    [Windows.Forms.Application]::DoEvents()
    try {
        $hwndStr = '0x' + $testForm.Handle.ToInt64().ToString('X')
        $insp = Run-Tool 'uia_inspect' @{ hwnd = $hwndStr; depth = 2 }
        Assert ([bool]$insp['ok'] -and [string]$insp['title'] -eq 'UIA_TEST_TARGET_WINDOW') 'uia_inspect by hwnd failed.'
        $kids = @($insp['children'])
        Assert ($kids.Count -ge 2) ('uia_inspect expected >= 2 child controls, got: ' + $kids.Count)

        $setTxt = Run-Tool 'uia_action' @{
            action = 'set_text'
            controlHwnd = $testBox.Handle.ToInt64().ToString()
            text = 'PO-2026-9941'
        }
        [Windows.Forms.Application]::DoEvents()
        Assert ([bool]$setTxt['ok'] -and $testBox.Text -eq 'PO-2026-9941') ('uia_action(set_text) failed, got: ' + $testBox.Text)

        $clkBtn = Run-Tool 'uia_action' @{
            action = 'click'
            controlHwnd = $testBtn.Handle.ToInt64().ToString()
        }
        [Windows.Forms.Application]::DoEvents()
        Assert ([bool]$clkBtn['ok'] -and $script:btnClicked) 'uia_action(click) via BM_CLICK did not trigger button click.'
    } finally {
        $testForm.Close()
        $testForm.Dispose()
    }

    # 8. Verify sessions_spawn & sessions_status (Lean Subagents + maxSpawnDepth = 2)
    $spawn1 = Run-Tool 'sessions_spawn' @{
        prompt = 'Cross-check Q4 scrap rows in vault.db'
        role = 'SQL Auditor Subagent'
        cli = 'local'
        depth = 1
    }
    Assert ([bool]$spawn1['ok'] -and [int]$spawn1['depth'] -eq 1 -and [bool]$spawn1['leanPrompt']) 'sessions_spawn depth 1 failed.'
    $jobId1 = [string]$spawn1['jobId']

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    $jobStatus = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        $jobStatus = Run-Tool 'sessions_status' @{ jobId = $jobId1 }
        if ([string]$jobStatus['status'] -eq 'completed') { break }
        Start-Sleep -Milliseconds 30
    }
    Assert ($null -ne $jobStatus -and [string]$jobStatus['status'] -eq 'completed') ('Subagent job did not complete in time: ' + $jobStatus['status'])
    Assert ([string]$jobStatus['output'] -like '*Cross-check Q4 scrap rows*') 'Subagent output mismatch.'

    # Spawning child at depth 2 from parent jobId1 must succeed, while depth 3 must be rejected!
    $spawn2 = Run-Tool 'sessions_spawn' @{
        prompt = 'Verify row 12'
        role = 'Leaf Verifier'
        cli = 'local'
        parentSessionId = $jobId1
    }
    Assert ([bool]$spawn2['ok'] -and [int]$spawn2['depth'] -eq 2) 'Child subagent at depth 2 should succeed.'
    $jobId2 = [string]$spawn2['jobId']

    $spawn3 = Run-Tool 'sessions_spawn' @{
        prompt = 'Depth 3 attempt'
        role = 'Too Deep'
        cli = 'local'
        parentSessionId = $jobId2
    }
    Assert (-not [bool]$spawn3['ok'] -and [string]$spawn3['error'] -like '*Max spawn depth*') 'sessions_spawn must reject depth > 2.'

    # 9. Verify 2 New Studio App Designs (Atelier & Nordic, Light + Dark each) via Theme.Look & MainForm
    $themeType.GetProperty('Look', $static).SetValue($null, 'atelier-light', $null)
    Assert ($themeType.GetProperty('Look', $static).GetValue($null, $null) -eq 'atelier' -and (-not [bool]$uiType.GetField('Dark', $static).GetValue($null))) 'Theme.Look = atelier-light failed.'
    Assert ((Hex-Of ($uiType.GetProperty('Bg', $static).GetValue($null, $null))) -eq '#F4F0E8') 'Atelier Light Bg mismatch.'
    Assert ((Hex-Of ($uiType.GetProperty('Card', $static).GetValue($null, $null))) -eq '#FCFAF5') 'Atelier Light Card mismatch.'
    Assert ((Hex-Of ($uiType.GetProperty('Accent', $static).GetValue($null, $null))) -eq '#D94822') 'Atelier Light Accent mismatch.'

    $themeType.GetProperty('Look', $static).SetValue($null, 'atelier-dark', $null)
    Assert ([bool]$uiType.GetField('Dark', $static).GetValue($null)) 'Theme.Look = atelier-dark did not enable Dark.'
    Assert ((Hex-Of ($uiType.GetProperty('Bg', $static).GetValue($null, $null))) -eq '#0C0E13') 'Atelier Dark Bg mismatch.'
    Assert ((Hex-Of ($uiType.GetProperty('Card', $static).GetValue($null, $null))) -eq '#13161F') 'Atelier Dark Card mismatch.'
    Assert ((Hex-Of ($uiType.GetProperty('Accent', $static).GetValue($null, $null))) -eq '#FF6238') 'Atelier Dark Accent mismatch.'

    $themeType.GetProperty('Look', $static).SetValue($null, 'nordic-light', $null)
    Assert ($themeType.GetProperty('Look', $static).GetValue($null, $null) -eq 'nordic' -and (-not [bool]$uiType.GetField('Dark', $static).GetValue($null))) 'Theme.Look = nordic-light failed.'
    Assert ((Hex-Of ($uiType.GetProperty('Bg', $static).GetValue($null, $null))) -eq '#F2F5F3') 'Nordic Light Bg mismatch.'
    Assert ((Hex-Of ($uiType.GetProperty('Card', $static).GetValue($null, $null))) -eq '#FFFFFF') 'Nordic Light Card mismatch.'
    Assert ((Hex-Of ($uiType.GetProperty('Accent', $static).GetValue($null, $null))) -eq '#0D8A64') 'Nordic Light Accent mismatch.'

    $themeType.GetProperty('Look', $static).SetValue($null, 'nordic-dark', $null)
    Assert ([bool]$uiType.GetField('Dark', $static).GetValue($null)) 'Theme.Look = nordic-dark did not enable Dark.'
    Assert ((Hex-Of ($uiType.GetProperty('Bg', $static).GetValue($null, $null))) -eq '#0D1311') 'Nordic Dark Bg mismatch.'
    Assert ((Hex-Of ($uiType.GetProperty('Card', $static).GetValue($null, $null))) -eq '#141D1A') 'Nordic Dark Card mismatch.'
    Assert ((Hex-Of ($uiType.GetProperty('Accent', $static).GetValue($null, $null))) -eq '#20C997') 'Nordic Dark Accent mismatch.'

    # Verify MainForm & AssistantPanel Rec button instantiate cleanly under Atelier and Nordic
    $mf = [Activator]::CreateInstance($asm.GetType('StickyPro.MainForm', $true), $true)
    try {
        $mf.GetType().GetMethod('SetAssistant', $instance).Invoke($mf, @($true)) | Out-Null
        $asstField = $mf.GetType().GetField('assistant', $instance)
        $asstPanel = $asstField.GetValue($mf)
        Assert ($null -ne $asstPanel) 'MainForm.SetAssistant(true) did not create AssistantPanel.'
        $recBtn = $asstPanel.GetType().GetField('btnRec', $instance).GetValue($asstPanel)
        Assert ($null -ne $recBtn) 'AssistantPanel.btnRec is missing.'
        $dir = $storeType.GetField('Dir', $static)
        $originalDir = $dir.GetValue($null)
        $blocked = Join-Path $workDir 'blocked-recording'
        $assistantStorage = Join-Path $blocked 'assistant'
        [void][IO.Directory]::CreateDirectory($assistantStorage)
        [IO.File]::WriteAllText((Join-Path $assistantStorage 'recordings'), 'Block the recording folder.')
        try {
            $dir.SetValue($null, $blocked)
            $asstPanel.ToggleRecording()
            Assert ($asstPanel.StatusText -like 'Could not start recording.*') 'Assistant hid the recording start error.'
            Assert (-not $recType.GetProperty('IsRecording', $static).GetValue($null, $null)) 'Blocked recording started anyway.'
        } finally { $dir.SetValue($null, $originalDir) }
    } finally {
        $mf.GetType().GetField('Exiting', $instance).SetValue($mf, $true)
        $mf.Dispose()
    }

    Write-Host 'WORKFLOW RECORDER, STUDIO DESIGNS & PHASE 4 PC TOOLS OK'
} finally {
    try { Invoke-S $recType 'Stop' @() | Out-Null } catch { }
    try { Invoke-S $vaultType 'Close' @() | Out-Null } catch { }
    if (Test-Path -LiteralPath $workDir) {
        try { Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    }
}
