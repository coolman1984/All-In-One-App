# Phase 2 verification for Live Office COM Control + Background Bulk COM Extractor.
# Exercises ApiTools schema, off-UI-thread CSV extraction, real Excel/Word/PowerPoint/PDF COM
# on disposable files in build-test\, MainForm.PageContext() live Excel keys, and zero process leaks.
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
function Invoke-S($type, [string]$method, [object[]]$arguments) {
    $raw = New-Object object[] $arguments.Count
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        $v = $arguments[$i]
        if ($null -eq $v) { $raw[$i] = $null } else { $raw[$i] = $v.PSObject.BaseObject }
    }
    $res = $type.GetMethod($method, $static).Invoke($null, $raw)
    return ,$res
}
function Get-Pids([string]$procName) {
    return @(Get-Process -Name $procName -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
}
function Wait-NoLeaked([string]$procName, [int[]]$beforePids, [string]$context) {
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 8) {
        $now = Get-Pids $procName
        $leaked = @($now | Where-Object { $beforePids -notcontains $_ })
        if ($leaked.Count -eq 0) { return }
        Start-Sleep -Milliseconds 150
    }
    $finalLeaked = @(Get-Pids $procName | Where-Object { $beforePids -notcontains $_ })
    throw ($context + ': leaked ' + $procName + ' process(es): ' + ($finalLeaked -join ', '))
}

$workDir = [IO.Directory]::CreateDirectory((Join-Path $root ('build-test\office-com-' + [guid]::NewGuid().ToString('N')))).FullName
$apiTools = $asm.GetType('StickyPro.ApiTools', $true)
$officeCom = $asm.GetType('StickyPro.OfficeCom', $true)
$officeEditType = $asm.GetType('StickyPro.OfficeEdit', $true)
$harness = $asm.GetType('StickyPro.AssistantHarness', $true)
$storeType = $asm.GetType('StickyPro.Store', $true)
$storeType.GetField('Dir', $static).SetValue($null, $workDir)
$storeType.GetField('ImgDir', $static).SetValue($null, (Join-Path $workDir 'images'))

Add-Type @'
using System;
using System.Globalization;
using System.Reflection;

public static class OfficeTestCom
{
    static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
    public static object Get(object o, string name, params object[] args)
    {
        object[] a = (args != null && args.Length > 0) ? args : null;
        return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, a, null, EnUs, null);
    }
    public static void Set(object o, string name, object val)
    {
        o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new object[] { val }, null, EnUs, null);
    }
    public static object Call(object o, string name, params object[] args)
    {
        object[] a = (args != null && args.Length > 0) ? args : null;
        return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, a, null, EnUs, null);
    }
    public static object CreateExcelWorkbook(object xlApp, string savePath)
    {
        Set(xlApp, "Visible", false);
        Set(xlApp, "DisplayAlerts", false);
        object wbs = Get(xlApp, "Workbooks");
        object wb = Call(wbs, "Add");
        Call(wb, "SaveAs", savePath);
        return wb;
    }
    public static object CreateWordDocument(object wdApp, string savePath)
    {
        Set(wdApp, "Visible", false);
        Set(wdApp, "DisplayAlerts", 0);
        object docs = Get(wdApp, "Documents");
        object doc = Call(docs, "Add");
        Call(doc, "SaveAs2", savePath);
        return doc;
    }
    public static void ExportWordPdf(object doc, string pdfPath)
    {
        Call(doc, "ExportAsFixedFormat", pdfPath, 17);
    }
    public static object OpenPptHidden(object pptApp, string path)
    {
        object presList = Get(pptApp, "Presentations");
        return Call(presList, "Open", path, 0, 0, 0);
    }
    public static bool CheckCellFormat(object wb, string cellAddr, int expectedBgr)
    {
        object ws = Get(wb, "ActiveSheet");
        object rng = Get(ws, "Range", cellAddr);
        bool bold = Convert.ToBoolean(Get(Get(rng, "Font"), "Bold"), CultureInfo.InvariantCulture);
        int color = Convert.ToInt32(Get(Get(rng, "Interior"), "Color"), CultureInfo.InvariantCulture);
        return bold && color == expectedBgr;
    }
}
'@

function New-AttachedEditor([string]$kind, [string]$path, $comApp, $comDoc, [bool]$startedPpt = $false) {
    $ed = [Activator]::CreateInstance($officeEditType, $true)
    $officeEditType.GetProperty('Kind', $instance).SetValue($ed, $kind, $null)
    $officeEditType.GetProperty('Path', $instance).SetValue($ed, $path, $null)
    $officeEditType.GetField('app', $instance).SetValue($ed, $comApp)
    $officeEditType.GetField('doc', $instance).SetValue($ed, $comDoc)
    $officeEditType.GetField('startedPowerPoint', $instance).SetValue($ed, $startedPpt)
    Assert ($null -ne $officeEditType.GetProperty('ComApp', $instance).GetValue($ed, $null)) 'OfficeEdit.ComApp was null.'
    Assert ($null -ne $officeEditType.GetProperty('ComDoc', $instance).GetValue($ed, $null)) 'OfficeEdit.ComDoc was null.'
    return $ed
}

$ranChecks = New-Object 'Collections.Generic.List[string]'

try {
    # 1. Verify all 11 new Office COM & extraction MCP tools in ApiTools.Names + Schema + AssistantHarness
    $names = $apiTools.GetProperty('Names', $static).GetValue($null, $null)
    $expectedTools = @(
        'excel_info', 'excel_read', 'excel_write', 'excel_formula', 'excel_sheet_add', 'excel_format',
        'word_read', 'word_write', 'ppt_read', 'ppt_write', 'office_extract'
    )
    foreach ($tn in $expectedTools) {
        Assert ($names.Contains($tn)) ('ApiTools.Names missing tool: ' + $tn)
    }

    $schemaList = Invoke-S $apiTools 'Schema' @()
    $excelWriteSchema = $schemaList | Where-Object { $_['name'] -eq 'excel_write' } | Select-Object -First 1
    $ewProps = $excelWriteSchema['inputSchema']['properties']
    Assert ($ewProps['values']['type'] -eq 'array' -and $ewProps['values']['items']['type'] -eq 'array') 'excel_write values schema is not 2D array.'

    $wordWriteSchema = $schemaList | Where-Object { $_['name'] -eq 'word_write' } | Select-Object -First 1
    $wwProps = $wordWriteSchema['inputSchema']['properties']
    Assert ($wwProps['rows']['type'] -eq 'array' -and $wwProps['rows']['items']['type'] -eq 'array') 'word_write rows schema is not 2D array.'

    $promptText = [string](Invoke-S $harness 'SystemPrompt' @($null, $null))
    foreach ($tn in @('excel_info', 'excel_read', 'excel_write', 'excel_formula', 'word_read', 'ppt_read', 'office_extract')) {
        Assert ($promptText.Contains($tn)) ('AssistantHarness.SystemPrompt missing tool: ' + $tn)
    }
    $ranChecks.Add('schema(11 tools)')

    # 2. Fast non-COM RFC-4180 CSV extraction + off-UI-thread ApiTools.Run('office_extract')
    $csvPath = Join-Path $workDir 'parts_inventory.csv'
    $csvContent = "Item,Qty,Notes,Qty`r`n`"Bolt, M8`",10,`"Grade `"`"A`"`" Steel`",10`r`n`"Nut`r`nHex`",25,`"Line1, Line2`",25`r`nWasher,100,Plain,100`r`n"
    [IO.File]::WriteAllText($csvPath, $csvContent, (New-Object Text.UTF8Encoding($false)))

    $csvDoc = Invoke-S $officeCom 'ExtractFile' @($csvPath, 25000)
    Assert ([string]::IsNullOrEmpty($csvDoc.Error)) ('CSV ExtractFile error: ' + $csvDoc.Error)
    Assert ($csvDoc.Kind -eq 'csv') ('Expected kind=csv, got ' + $csvDoc.Kind)
    Assert (-not $csvDoc.IsDrm) 'Plain CSV marked as DRM.'
    Assert ($csvDoc.FileSize -gt 0 -and $csvDoc.ModifiedTicks -gt 0) 'CSV metadata missing.'
    Assert ($csvDoc.Tables.Count -eq 1) ('Expected 1 CSV table, got ' + $csvDoc.Tables.Count)
    $t0 = $csvDoc.Tables[0]
    Assert ($t0.Name -eq 'parts_inventory') ('Unexpected CSV table name: ' + $t0.Name)
    Assert ($t0.TotalRows -eq 3 -and $t0.TotalCols -eq 4) ('Unexpected CSV dimensions: ' + $t0.TotalRows + 'x' + $t0.TotalCols)
    Assert ($t0.Columns[0] -eq 'Item' -and $t0.Columns[3] -eq 'Qty_2') ('Duplicate column dedup failed: ' + ($t0.Columns -join ','))
    Assert ($t0.Rows[0][0] -eq 'Bolt, M8') ('CSV quoted comma failed: ' + $t0.Rows[0][0])
    Assert ($t0.Rows[0][2] -eq 'Grade "A" Steel') ('CSV escaped quote failed: ' + $t0.Rows[0][2])
    Assert ($t0.Rows[1][0].Contains("Nut") -and $t0.Rows[1][0].Contains("Hex")) ('CSV embedded newline failed: ' + $t0.Rows[1][0])
    Assert ($csvDoc.FullText.Contains('Washer')) 'CSV FullText missing row data.'

    # Invoke ApiTools.Run('office_extract') directly with App.I == null to prove it runs OFF the UI thread.
    $extArgs = New-Object 'Collections.Generic.Dictionary[string,object]'
    $extArgs['path'] = $csvPath
    $extArgs['preview_rows'] = 2
    $extJson = [string](Invoke-S $apiTools 'Run' @('office_extract', $extArgs))
    Assert ($extJson.Contains('"kind":"csv"') -and $extJson.Contains('"total_rows":3') -and $extJson.Contains('Bolt, M8')) ('office_extract JSON unexpected: ' + $extJson)
    $ranChecks.Add('csv-direct+off-ui-mcp')

    # 3. Real Excel COM live control + PageContext + ExtractFromLiveEditor + ExtractFile(.xlsx) + zero leak
    if ($null -ne [Type]::GetTypeFromProgID('Excel.Application')) {
        $excelBefore = Get-Pids 'EXCEL'
        $xlsxPath = Join-Path $workDir 'live_test.xlsx'
        $xlApp = [Activator]::CreateInstance([Type]::GetTypeFromProgID('Excel.Application'))
        $ed = $null
        try {
            $wb = [OfficeTestCom]::CreateExcelWorkbook($xlApp, $xlsxPath)
            $ed = New-AttachedEditor 'excel' $xlsxPath $xlApp $wb

            # ExcelQuickContext
            $qctx = Invoke-S $officeCom 'ExcelQuickContext' @($ed)
            Assert ($null -ne $qctx -and -not [string]::IsNullOrEmpty($qctx['excel_active_sheet'])) 'ExcelQuickContext missing active sheet.'
            Assert ($qctx['excel_sheets'].Count -ge 1) 'ExcelQuickContext missing sheets.'

            # ExcelWrite with mixed values + formulas
            $vals = New-Object 'Collections.Generic.List[Collections.Generic.List[object]]'
            $r1 = New-Object 'Collections.Generic.List[object]'; foreach ($x in @('Item','Qty','Price','Total')) { $r1.Add($x) }
            $r2 = New-Object 'Collections.Generic.List[object]'; foreach ($x in @('Bolt',10,2.5,'=B2*C2')) { $r2.Add($x) }
            $r3 = New-Object 'Collections.Generic.List[object]'; foreach ($x in @('Nut',20,1.5,'=B3*C3')) { $r3.Add($x) }
            $r4 = New-Object 'Collections.Generic.List[object]'; foreach ($x in @('Gear',5,10,'=B4*C4')) { $r4.Add($x) }
            $vals.Add($r1); $vals.Add($r2); $vals.Add($r3); $vals.Add($r4)

            $wres = Invoke-S $officeCom 'ExcelWrite' @($ed, '', 'A1', $vals, $false)
            Assert ($wres['rows_written'] -eq 4 -and $wres['cols_written'] -eq 4 -and $wres['range'] -eq 'A1:D4') ('ExcelWrite result mismatch: ' + ($wres | ConvertTo-Json -Compress))

            # ExcelFormula (=SUM)
            $fres = Invoke-S $officeCom 'ExcelFormula' @($ed, '', 'D5', '=SUM(D2:D4)', $false)
            Assert ($fres['value'] -eq '105') ('ExcelFormula =SUM expected 105, got: ' + $fres['value'])

            # ExcelRead range + single 1x1 cell (non-array Value2)
            $rres = Invoke-S $officeCom 'ExcelRead' @($ed, '', 'A1:D5', 200, 40)
            Assert ($rres['total_rows'] -eq 5 -and $rres['total_cols'] -eq 4) 'ExcelRead range dimensions mismatch.'
            Assert ($rres['headers'][0] -eq 'Item' -and $rres['rows'][1][3] -eq '25' -and $rres['rows'][4][3] -eq '105') 'ExcelRead cell values mismatch.'

            $singleRes = Invoke-S $officeCom 'ExcelRead' @($ed, '', 'D5', 200, 40)
            Assert ($singleRes['total_rows'] -eq 1 -and $singleRes['total_cols'] -eq 1 -and $singleRes['rows'][0][0] -eq '105') 'ExcelRead 1x1 scalar handling failed.'

            # ExcelFormat (bold + BGR hex bg + font hex + numberFormat + autoFit)
            $fmtRes = Invoke-S $officeCom 'ExcelFormat' @($ed, '', 'A1:D1', $true, '#EAF1FF', '#1E293B', '@', $true)
            Assert ($fmtRes['formatted'] -eq $true) 'ExcelFormat failed.'
            $expectedBgr = (0xFF -shl 16) -bor (0xF1 -shl 8) -bor 0xEA
            Assert ([OfficeTestCom]::CheckCellFormat($wb, 'A1', $expectedBgr)) 'ExcelFormat did not apply Font.Bold or expected BGR Interior.Color.'

            # ExcelSheetAdd with illegal chars + write + save
            $addRes = Invoke-S $officeCom 'ExcelSheetAdd' @($ed, 'Summary/Q4?', $true)
            Assert ($addRes['sheet'] -eq 'Summary_Q4_' -and $addRes['sheets'].Count -ge 2) ('ExcelSheetAdd mismatch: ' + $addRes['sheet'])

            $vals2 = New-Object 'Collections.Generic.List[Collections.Generic.List[object]]'
            $sr1 = New-Object 'Collections.Generic.List[object]'; $sr1.Add('Metric'); $sr1.Add('Value')
            $sr2 = New-Object 'Collections.Generic.List[object]'; $sr2.Add('GrandTotal'); $sr2.Add(105)
            $vals2.Add($sr1); $vals2.Add($sr2)
            $wres2 = Invoke-S $officeCom 'ExcelWrite' @($ed, 'Summary_Q4_', 'A1', $vals2, $true)
            Assert ($wres2['saved'] -eq $true) 'ExcelWrite save=true did not save workbook.'

            # ExcelInfo
            $infoRes = Invoke-S $officeCom 'ExcelInfo' @($ed)
            Assert ($infoRes['active_sheet'] -eq 'Summary_Q4_' -and $infoRes['sheets'].Count -ge 2) 'ExcelInfo mismatch.'

            # ExtractFromLiveEditor
            $liveDoc = Invoke-S $officeCom 'ExtractFromLiveEditor' @($ed, 25000)
            Assert ([string]::IsNullOrEmpty($liveDoc.Error) -and $liveDoc.Tables.Count -ge 2) ('ExtractFromLiveEditor failed: ' + $liveDoc.Error)
            Assert ($liveDoc.FullText.Contains('GrandTotal') -and $liveDoc.FullText.Contains('105')) 'ExtractFromLiveEditor missing sheet text.'

            # MainForm.PageContext() live Excel integration
            $main = [Activator]::CreateInstance($asm.GetType('StickyPro.MainForm', $true), $true)
            try {
                $main.ShowPage('documents')
                $dv = $main.DocumentsView
                $editsMap = $dv.GetType().GetField('edits', $instance).GetValue($dv)
                $editsMap[$xlsxPath] = $ed
                $dv.GetType().GetField('current', $instance).SetValue($dv, $xlsxPath)
                $pctx = $main.PageContext()
                Assert ($pctx.ContainsKey('excel_active_sheet') -and $pctx['excel_active_sheet'] -eq 'Summary_Q4_') 'PageContext missing excel_active_sheet.'
                Assert ($pctx.ContainsKey('excel_selection')) 'PageContext missing excel_selection.'
                Assert ($pctx.ContainsKey('excel_sheets') -and $pctx['excel_sheets'].Count -ge 2) 'PageContext missing excel_sheets.'
                $editsMap.Remove($xlsxPath) | Out-Null
            } finally {
                $main.Dispose()
            }
        } finally {
            if ($null -ne $ed) { $ed.Close($false) | Out-Null; $ed.Dispose() }
            else {
                try { if ($null -ne $wb) { [OfficeTestCom]::Call($wb, 'Close', @($false)) | Out-Null } } catch {}
                try { if ($null -ne $xlApp) { [OfficeTestCom]::Call($xlApp, 'Quit', @()) | Out-Null } } catch {}
            }
            $wb = $null; $xlApp = $null; $ed = $null
        }
        Wait-NoLeaked 'EXCEL' $excelBefore 'After live Excel test'

        # Background ExtractFile(.xlsx)
        $xDoc = Invoke-S $officeCom 'ExtractFile' @($xlsxPath, 25000)
        Assert ([string]::IsNullOrEmpty($xDoc.Error)) ('Excel ExtractFile error: ' + $xDoc.Error)
        Assert ($xDoc.Kind -eq 'excel' -and $xDoc.Tables.Count -ge 2) 'Excel ExtractFile did not read all sheets.'
        $sheet1 = $xDoc.Tables[0]
        Assert ($sheet1.Columns[0] -eq 'Item' -and $sheet1.Rows[0][3] -eq '25') ('Excel ExtractFile sheet 1 mismatch: ' + ($sheet1.Rows[0] -join ','))
        $sumSheet = $xDoc.Tables | Where-Object { $_.Name -eq 'Summary_Q4_' } | Select-Object -First 1
        Assert ($null -ne $sumSheet -and $sumSheet.Rows[0][0] -eq 'GrandTotal' -and $sumSheet.Rows[0][1] -eq '105') 'Excel ExtractFile Summary_Q4_ mismatch.'
        Wait-NoLeaked 'EXCEL' $excelBefore 'After Excel ExtractFile'
        $ranChecks.Add('excel-live+page-context+extract+no-leak')
    }

    # 4. Real Word COM live control + ExtractFile(.docx) + ExtractFile(.pdf) + zero leak
    if ($null -ne [Type]::GetTypeFromProgID('Word.Application')) {
        $wordBefore = Get-Pids 'WINWORD'
        $docxPath = Join-Path $workDir 'live_test.docx'
        $pdfPath = Join-Path $workDir 'live_test.pdf'
        $wdApp = [Activator]::CreateInstance([Type]::GetTypeFromProgID('Word.Application'))
        $ed = $null
        try {
            $wdoc = [OfficeTestCom]::CreateWordDocument($wdApp, $docxPath)
            $ed = New-AttachedEditor 'word' $docxPath $wdApp $wdoc

            $aRes = Invoke-S $officeCom 'WordWrite' @($ed, 'append', 'Factory Line A status: PENDING', '', $null, $false)
            Assert ($aRes['mode'] -eq 'append') 'WordWrite append failed.'

            $repRes = Invoke-S $officeCom 'WordWrite' @($ed, 'replace', 'APPROVED', 'PENDING', $null, $false)
            Assert ($repRes['replaced'] -eq $true) 'WordWrite replace did not match PENDING.'

            $tRows = New-Object 'Collections.Generic.List[Collections.Generic.List[string]]'
            $tr1 = New-Object 'Collections.Generic.List[string]'; foreach ($x in @('Machine','Shift','Output')) { $tr1.Add($x) }
            $tr2 = New-Object 'Collections.Generic.List[string]'; foreach ($x in @('Press-1','Day','420')) { $tr2.Add($x) }
            $tr3 = New-Object 'Collections.Generic.List[string]'; foreach ($x in @('Press-2','Night','380')) { $tr3.Add($x) }
            $tRows.Add($tr1); $tRows.Add($tr2); $tRows.Add($tr3)
            $tblRes = Invoke-S $officeCom 'WordWrite' @($ed, 'insert_table', $null, '', $tRows, $true)
            Assert ($tblRes['rows_inserted'] -eq 3 -and $tblRes['cols_inserted'] -eq 3 -and $tblRes['saved'] -eq $true) 'WordWrite insert_table failed.'

            $wr = Invoke-S $officeCom 'WordRead' @($ed, 30000)
            Assert ($wr['text'].Contains('Factory Line A status: APPROVED') -and -not $wr['text'].Contains('PENDING')) ('WordRead text mismatch: ' + $wr['text'])
            Assert ($wr['table_count'] -eq 1 -and $wr['tables'][0]['rows'][1][2] -eq '420') 'WordRead table mismatch.'

            # Export a real text PDF for PDF extraction test
            [OfficeTestCom]::ExportWordPdf($wdoc, $pdfPath)
        } finally {
            if ($null -ne $ed) { $ed.Close($false) | Out-Null; $ed.Dispose() }
            else {
                try { if ($null -ne $wdoc) { [OfficeTestCom]::Call($wdoc, 'Close', @(0)) | Out-Null } } catch {}
                try { if ($null -ne $wdApp) { [OfficeTestCom]::Call($wdApp, 'Quit', @(0)) | Out-Null } } catch {}
            }
            $wdoc = $null; $wdApp = $null; $ed = $null
        }
        Wait-NoLeaked 'WINWORD' $wordBefore 'After live Word test'

        $wDocOut = Invoke-S $officeCom 'ExtractFile' @($docxPath, 25000)
        Assert ([string]::IsNullOrEmpty($wDocOut.Error)) ('Word ExtractFile error: ' + $wDocOut.Error)
        Assert ($wDocOut.Kind -eq 'word' -and $wDocOut.FullText.Contains('APPROVED') -and $wDocOut.Sections.Count -ge 1) 'Word ExtractFile text/sections mismatch.'
        Assert ($wDocOut.Tables.Count -eq 1 -and $wDocOut.Tables[0].Columns[0] -eq 'Machine' -and $wDocOut.Tables[0].TotalRows -eq 2) 'Word ExtractFile table mismatch.'
        Wait-NoLeaked 'WINWORD' $wordBefore 'After Word ExtractFile'

        if ([IO.File]::Exists($pdfPath)) {
            $pdfDocOut = Invoke-S $officeCom 'ExtractFile' @($pdfPath, 25000)
            Assert ([string]::IsNullOrEmpty($pdfDocOut.Error)) ('PDF ExtractFile error: ' + $pdfDocOut.Error)
            Assert ($pdfDocOut.Kind -eq 'pdf' -and $pdfDocOut.FullText.Contains('APPROVED')) ('PDF ExtractFile text mismatch: ' + $pdfDocOut.FullText)
            Wait-NoLeaked 'WINWORD' $wordBefore 'After PDF ExtractFile'
            $ranChecks.Add('word-live+docx-extract+pdf-extract+no-leak')
        } else {
            $ranChecks.Add('word-live+docx-extract+no-leak')
        }
    }

    # 5. Real PowerPoint COM live control + ExtractFile(.pptx) + shared process safety
    $templatePptx = Join-Path $root 'src\ChatSlides.pptx'
    if ($null -ne [Type]::GetTypeFromProgID('PowerPoint.Application') -and [IO.File]::Exists($templatePptx)) {
        $pptBefore = Get-Pids 'POWERPNT'
        $pptxPath = Join-Path $workDir 'live_test.pptx'
        [IO.File]::Copy($templatePptx, $pptxPath, $true)
        $startedPpt = ($pptBefore.Count -eq 0)
        $pptApp = [Activator]::CreateInstance([Type]::GetTypeFromProgID('PowerPoint.Application'))
        $ed = $null
        try {
            $pres = [OfficeTestCom]::OpenPptHidden($pptApp, $pptxPath)
            $ed = New-AttachedEditor 'powerpoint' $pptxPath $pptApp $pres $startedPpt

            $pr0 = Invoke-S $officeCom 'PptRead' @($ed)
            $origCount = [int]$pr0['slide_count']

            $pw1 = Invoke-S $officeCom 'PptWrite' @($ed, 0, 'Phase 2 COM Title', 'Bullet item alpha', $true, $true)
            Assert ($pw1['added'] -eq $true -and $pw1['saved'] -eq $true -and $pw1['slide_count'] -eq ($origCount + 1)) 'PptWrite add_slide failed.'
            $newSlideNum = [int]$pw1['slide']

            $pw2 = Invoke-S $officeCom 'PptWrite' @($ed, $newSlideNum, 'Updated Slide Heading', 'Updated bullet beta', $false, $true)
            Assert ($pw2['added'] -eq $false -and $pw2['saved'] -eq $true) 'PptWrite edit slide failed.'

            $pr1 = Invoke-S $officeCom 'PptRead' @($ed)
            $lastSlide = $pr1['slides'][$pr1['slides'].Count - 1]
            Assert ($lastSlide['title'] -eq 'Updated Slide Heading') ('PptRead title mismatch: ' + $lastSlide['title'])
            Assert ((($lastSlide['texts']) -join ' ').Contains('Updated bullet beta')) 'PptRead body mismatch.'
        } finally {
            if ($null -ne $ed) { $ed.Close($false) | Out-Null; $ed.Dispose() }
            else {
                try { if ($null -ne $pres) { [OfficeTestCom]::Set($pres, 'Saved', -1); [OfficeTestCom]::Call($pres, 'Close', @()) | Out-Null } } catch {}
                try { if ($null -ne $pptApp -and $startedPpt) { [OfficeTestCom]::Call($pptApp, 'Quit', @()) | Out-Null } } catch {}
            }
            $pres = $null; $pptApp = $null; $ed = $null
        }
        Wait-NoLeaked 'POWERPNT' $pptBefore 'After live PowerPoint test'

        $pDocOut = Invoke-S $officeCom 'ExtractFile' @($pptxPath, 25000)
        Assert ([string]::IsNullOrEmpty($pDocOut.Error)) ('PowerPoint ExtractFile error: ' + $pDocOut.Error)
        Assert ($pDocOut.Kind -eq 'powerpoint' -and $pDocOut.Sections.Count -ge 1) 'PowerPoint ExtractFile sections mismatch.'
        Assert ($pDocOut.FullText.Contains('Updated Slide Heading') -and $pDocOut.FullText.Contains('Updated bullet beta')) ('PowerPoint ExtractFile text mismatch: ' + $pDocOut.FullText)
        Wait-NoLeaked 'POWERPNT' $pptBefore 'After PowerPoint ExtractFile'
        $ranChecks.Add('ppt-live+extract+no-leak')
    }

    Write-Host ('OFFICE COM OK: ' + ($ranChecks -join ', '))
}
finally {
    try { if ([IO.Directory]::Exists($workDir)) { [IO.Directory]::Delete($workDir, $true) } } catch {}
}
