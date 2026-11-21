# Phase 3 verification for Native Windows SQLite Vault (winsqlite3.dll).
# Exercises WAL mode, schema creation, UTF-8 Arabic/Unicode round-tripping, two-report extraction,
# cache hit on unchanged re-extract, cross-report SQL JOIN with numeric aggregation, FTS search,
# session search with Temporal Decay + MMR dedup, step_events + ForkAt, alert_dedup, QueueExtract,
# and clean DB close with zero file-lock leaks.
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
function Invoke-S($type, [string]$method, [object[]]$arguments) {
    $raw = New-Object object[] $arguments.Count
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        $v = $arguments[$i]
        if ($null -eq $v) { $raw[$i] = $null } else { $raw[$i] = $v.PSObject.BaseObject }
    }
    $res = $type.GetMethod($method, $static).Invoke($null, $raw)
    return ,$res
}

$workDir = [IO.Directory]::CreateDirectory((Join-Path $root ('build-test\sqlite-vault-' + [guid]::NewGuid().ToString('N')))).FullName
$storeType = $asm.GetType('StickyPro.Store', $true)
$vaultType = $asm.GetType('StickyPro.SqliteVault', $true)
$apiTools = $asm.GetType('StickyPro.ApiTools', $true)
$js = New-Object Web.Script.Serialization.JavaScriptSerializer

$storeType.GetField('Dir', $static).SetValue($null, $workDir)
$storeType.GetField('ImgDir', $static).SetValue($null, (Join-Path $workDir 'images'))

# Build Arabic UTF-8 word cleanly from Unicode code points (ASCII-safe PowerShell 5 script).
$arabicWord = ([string][char]0x062A) + ([string][char]0x0642) + ([string][char]0x0631) + ([string][char]0x064A) + ([string][char]0x0631) # "taqrir" (report)

try {
    # 1. WAL mode + schema creation + FTS detection on temp vault.db
    $journalMode = [string](Invoke-S $vaultType 'JournalMode' @())
    Assert ($journalMode.ToLowerInvariant() -eq 'wal') ('Expected WAL journal_mode, got: ' + $journalMode)

    $ftsMode = [string]$vaultType.GetProperty('FtsMode', $static).GetValue($null, $null)
    Assert ($ftsMode -eq 'fts5' -or $ftsMode -eq 'fts4' -or $ftsMode -eq 'plain') ('Unexpected FtsMode: ' + $ftsMode)

    $dbPath = [string]$vaultType.GetProperty('DbPath', $static).GetValue($null, $null)
    Assert ([IO.File]::Exists($dbPath)) ('vault.db was not created at: ' + $dbPath)

    $tablesCheck = Invoke-S $vaultType 'QuerySql' @("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;", 100)
    $existingNames = @($tablesCheck['rows'] | ForEach-Object { [string]$_[0] })
    foreach ($reqTbl in @('vault_docs', 'vault_tables', 'vault_fts', 'session_msgs', 'session_fts', 'step_events', 'alert_dedup')) {
        Assert ($existingNames -contains $reqTbl) ('Missing required SQLite table: ' + $reqTbl)
    }

    # 2. Extract TWO separate multi-row CSV reports (sales.csv + stock.csv) with Unicode/Arabic text
    $salesPath = Join-Path $workDir 'sales.csv'
    $stockPath = Join-Path $workDir 'stock.csv'
    $utf8 = New-Object Text.UTF8Encoding($false)

    $salesCsv = "sku,item_name,qty,unit_price`r`n" +
        "SKU-100,Motor " + $arabicWord + ",12,25.5`r`n" +
        "SKU-200,Pump Valve,5,40.0`r`n" +
        "SKU-300,Bearing Kit,8,15.0`r`n"
    $stockCsv = "sku,warehouse,on_hand,reorder_min`r`n" +
        "SKU-100,Cairo-A,30,10`r`n" +
        "SKU-200,Cairo-A,4,10`r`n" +
        "SKU-300,Alex-B,20,5`r`n"

    [IO.File]::WriteAllText($salesPath, $salesCsv, $utf8)
    [IO.File]::WriteAllText($stockPath, $stockCsv, $utf8)

    $r1 = Invoke-S $vaultType 'ExtractAndStore' @($salesPath, $false, 25000)
    Assert (-not [bool]$r1['cached']) 'First sales.csv extract should not be cached.'
    Assert ([int]$r1['row_count'] -eq 3) ('Expected 3 sales rows, got ' + $r1['row_count'])
    $salesTable = [string]$r1['tables'][0]['sql_table']
    Assert ($salesTable.StartsWith('data_') -and $salesTable.EndsWith('_sales')) ('Unexpected sales SQL table: ' + $salesTable)

    # Confirm cache hit on unchanged re-extract (0 COM/parse work)
    $r1Cached = Invoke-S $vaultType 'ExtractAndStore' @($salesPath, $false, 25000)
    Assert ([bool]$r1Cached['cached']) 'Unchanged sales.csv re-extract did not hit cache.'
    Assert ([long]$r1Cached['doc_id'] -eq [long]$r1['doc_id']) 'Cached doc_id mismatch.'

    $r2 = Invoke-S $vaultType 'ExtractAndStore' @($stockPath, $false, 25000)
    Assert (-not [bool]$r2['cached']) 'First stock.csv extract should not be cached.'
    $stockTable = [string]$r2['tables'][0]['sql_table']
    Assert ($stockTable.StartsWith('data_') -and $stockTable.EndsWith('_stock')) ('Unexpected stock SQL table: ' + $stockTable)

    # 3. Cross-report SQL JOIN + numeric SUM/multiplication + Arabic UTF-8 round-trip verification!
    $joinSql = "SELECT s.sku, s.item_name, s.qty, k.on_hand, (s.qty * s.unit_price) AS revenue " +
        "FROM `"$salesTable`" s JOIN `"$stockTable`" k ON s.sku = k.sku ORDER BY s.sku;"
    $joinRes = Invoke-S $vaultType 'QuerySql' @($joinSql, 500)
    Assert ([int]$joinRes['row_count'] -eq 3) ('Expected 3 joined rows, got ' + $joinRes['row_count'])
    $row0 = $joinRes['rows'][0]
    Assert ([string]$row0[0] -eq 'SKU-100') ('Joined row0 SKU mismatch: ' + $row0[0])
    Assert ([string]$row0[1] -eq ('Motor ' + $arabicWord)) ('UTF-8 Arabic text corrupted in SQLite round-trip: ' + $row0[1])
    Assert ([long]$row0[2] -eq 12) ('Joined row0 qty mismatch: ' + $row0[2])
    Assert ([long]$row0[3] -eq 30) ('Joined row0 on_hand mismatch: ' + $row0[3])
    Assert ([Math]::Abs([double]$row0[4] - 306.0) -lt 0.001) ('Joined row0 numeric revenue mismatch: ' + $row0[4])

    # Numeric aggregation across joined tables via off-UI-thread MCP vault_query
    $aggArgs = New-Object 'Collections.Generic.Dictionary[string,object]'
    $aggArgs['sql'] = "SELECT SUM(s.qty) AS total_qty, SUM(k.on_hand) AS total_stock FROM `"$salesTable`" s JOIN `"$stockTable`" k ON s.sku = k.sku;"
    $aggJson = [string](Invoke-S $apiTools 'Run' @('vault_query', $aggArgs))
    $aggObj = $js.DeserializeObject($aggJson)
    Assert ([int]$aggObj['rows'][0][0] -eq 25) ('Total qty SUM expected 25, got ' + $aggObj['rows'][0][0])
    Assert ([int]$aggObj['rows'][0][1] -eq 54) ('Total stock SUM expected 54, got ' + $aggObj['rows'][0][1])

    # Verify vault_list and vault_schema MCP tools
    $docsList = Invoke-S $vaultType 'ListDocs' @('', 50)
    Assert ($docsList.Count -eq 2) ('Expected 2 indexed docs, got ' + $docsList.Count)
    $schemaAll = Invoke-S $vaultType 'GetSchema' @([long]0)
    Assert ($schemaAll.Count -eq 2) ('Expected 2 vault_tables in GetSchema(0), got ' + $schemaAll.Count)

    # 4. Full-text vault_search (both English and Arabic)
    $hitsEn = Invoke-S $vaultType 'SearchVault' @('Bearing', 20)
    Assert ($hitsEn.Count -ge 1 -and [string]$hitsEn[0]['file_name'] -eq 'sales.csv') 'vault_search did not find Bearing in sales.csv.'
    $hitsAr = Invoke-S $vaultType 'SearchVault' @($arabicWord, 20)
    Assert ($hitsAr.Count -ge 1 -and [string]$hitsAr[0]['file_name'] -eq 'sales.csv') 'vault_search did not find Arabic word in sales.csv.'

    # 5. Session indexing + SearchSessions (Temporal Decay preferring newer match + MMR dedup!)
    $nowTicks = [DateTime]::Now.Ticks
    $oldTicks = [DateTime]::Now.AddDays(-90).Ticks

    $null = Invoke-S $vaultType 'IndexSessionMessage' @('chat-old', 'assistant', 'agent', 'claude', 'Quarterly turbine maintenance budget approved for line alpha with spare parts reserve', $oldTicks)
    $null = Invoke-S $vaultType 'IndexSessionMessage' @('chat-new', 'assistant', 'agent', 'claude', 'Quarterly turbine maintenance budget approved for line beta with calibration schedule', $nowTicks)
    # Near-duplicate of chat-new (Jaccard > 0.75) should be filtered out by MMR dedup!
    $dupTicks = $nowTicks - 1000
    $null = Invoke-S $vaultType 'IndexSessionMessage' @('chat-dup', 'assistant', 'agent', 'codex', 'Quarterly turbine maintenance budget approved for line beta with calibration schedule updated', $dupTicks)
    # Different scope ('agents') should be filterable
    $null = Invoke-S $vaultType 'IndexSessionMessage' @('chat-code', 'agents', 'user', '', 'Turbine PLC firmware C# refactor notes', $nowTicks)

    $sessHits = Invoke-S $vaultType 'SearchSessions' @('turbine maintenance budget', 'assistant', 10)
    Assert ($sessHits.Count -eq 2) ('Expected 2 MMR-deduplicated assistant session hits (dup removed), got ' + $sessHits.Count)
    Assert ([string]$sessHits[0]['chat_id'] -eq 'chat-new') ('Temporal decay should rank chat-new above 90-day-old chat-old; got: ' + $sessHits[0]['chat_id'])
    Assert ([double]$sessHits[0]['score'] -gt [double]$sessHits[1]['score']) 'Newer session hit score was not higher than older hit score.'

    $agentScopeHits = Invoke-S $vaultType 'SearchSessions' @('turbine', 'agents', 10)
    Assert ($agentScopeHits.Count -eq 1 -and [string]$agentScopeHits[0]['chat_id'] -eq 'chat-code') 'Scope filter for agents failed.'

    # 6. Event-sourced step_events + ForkAt
    $t2 = $nowTicks + 10
    $t3 = $nowTicks + 20
    $s1 = [long](Invoke-S $vaultType 'RecordStep' @('chat-trace', 1, 1, 'user', '', '{"msg":"start"}', $nowTicks))
    $s2 = [long](Invoke-S $vaultType 'RecordStep' @('chat-trace', 1, 2, 'tool_call', 'vault_query', '{"sql":"SELECT 1"}', $t2))
    $s3 = [long](Invoke-S $vaultType 'RecordStep' @('chat-trace', 2, 1, 'agent', 'claude', '{"text":"done"}', $t3))
    Assert ($s1 -gt 0 -and $s2 -gt $s1 -and $s3 -gt $s2) 'RecordStep did not return ascending IDs.'

    $steps = Invoke-S $vaultType 'GetSteps' @('chat-trace', 50)
    Assert ($steps.Count -eq 3) ('Expected 3 steps, got ' + $steps.Count)

    $forkRes = Invoke-S $vaultType 'ForkAt' @('chat-trace', $s2, 'chat-trace-branch')
    Assert ([int]$forkRes['copied_steps'] -eq 2) ('Expected 2 copied steps on ForkAt, got ' + $forkRes['copied_steps'])
    $branchSteps = Invoke-S $vaultType 'GetSteps' @('chat-trace-branch', 50)
    Assert ($branchSteps.Count -eq 2 -and [string]$branchSteps[1]['tool_name'] -eq 'vault_query') 'Forked branch steps mismatch.'

    # 7. Alert deduplication (alert_dedup)
    $cooldown = [TimeSpan]::FromHours(6)
    Assert (-not [bool](Invoke-S $vaultType 'HasAlerted' @('hash-alert-001', $cooldown))) 'Fresh alert hash should not be marked alerted.'
    Invoke-S $vaultType 'MarkAlerted' @('hash-alert-001', 'heartbeat:test') | Out-Null
    Assert ([bool](Invoke-S $vaultType 'HasAlerted' @('hash-alert-001', $cooldown))) 'Marked alert hash should be detected within cooldown.'
    Assert (-not [bool](Invoke-S $vaultType 'HasAlerted' @('hash-alert-001', [TimeSpan]::Zero))) 'Zero cooldown should not suppress alert.'

    # 8. Lazy background QueueExtract on a third CSV report
    $shipPath = Join-Path $workDir 'shipments.csv'
    [IO.File]::WriteAllText($shipPath, "sku,shipped`r`nSKU-100,7`r`n", $utf8)
    Invoke-S $vaultType 'QueueExtract' @($shipPath) | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $foundShip = $false
    while ($sw.Elapsed.TotalSeconds -lt 10) {
        $dl = Invoke-S $vaultType 'ListDocs' @('shipments.csv', 10)
        if ($dl.Count -ge 1) { $foundShip = $true; break }
        Start-Sleep -Milliseconds 50
    }
    Assert $foundShip 'QueueExtract did not index shipments.csv in background.'
} finally {
    # 9. Close DB cleanly and verify temp directory deletes with ZERO file-lock errors!
    Invoke-S $vaultType 'Close' @() | Out-Null
    Start-Sleep -Milliseconds 50
    [IO.Directory]::Delete($workDir, $true)
    Assert (-not [IO.Directory]::Exists($workDir)) 'Temp vault directory was not cleanly deleted.'
}

Write-Host ('SQLITE VAULT OK: WAL + ' + $ftsMode + ' + UTF-8 Arabic + cache hit + cross-report SQL JOIN + Decay/MMR session search + step_events ForkAt + alert_dedup + QueueExtract + clean Close')
