# Isolated chats, terminal launches and document fixtures.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.IO.Compression.FileSystem
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$instance = [Reflection.BindingFlags]'Public,NonPublic,Instance'
function TypeOf($name) { $assembly.GetType('StickyPro.' + $name, $true) }
function NewRecord($name) { [Activator]::CreateInstance((TypeOf $name)) }
function Assert($ok, $reason) { if (!$ok) { throw $reason } }
$fixture = [IO.Directory]::CreateDirectory((Join-Path $root ('build-test\saved-work-' + [guid]::NewGuid().ToString('N')))).FullName
(TypeOf Store).GetField('Dir',$static).SetValue($null,$fixture)
$store = TypeOf AgentStore
$chat = NewRecord AgentChat
$chat.Id = 'draft-test'; $chat.Folder = $fixture
$draft = $store.GetMethod('SetDraft',$static)
$draft.Invoke($null,@($chat,'unsent'))
Assert ($store.GetField('Chats',$static).GetValue($null).Contains($chat)) 'First draft missing from library'
$chat.Fav = $true; $chat.Collection = 'Design'; $chat.Archived = $true
$store.GetMethod('Save',$static).Invoke($null,@($chat))
$draft.Invoke($null,@($chat,"unsent revised`nsecond line"))
Assert ($store.GetMethod('FlushWrites',$static).Invoke($null,@())) 'Flush failed'
$file = Join-Path $fixture 'agents\chats\draft-test.json'
$read = $store.GetMethod('ReadChat',$static)
$copy = $read.Invoke($null,@([string]$file))
Assert ($copy.Fav -and $copy.Archived -and $copy.Collection -eq 'Design') 'Chat organization did not round trip'
Assert ($copy.GetType().GetField('PendingInput',$instance).GetValue($copy) -ceq "unsent revised`nsecond line") 'Draft did not survive reload'
$draft.Invoke($null,@($chat,''))
Assert ($store.GetMethod('FlushWrites',$static).Invoke($null,@())) 'Clearing draft failed'
Assert ($copy.GetType().GetField('PendingInput',$instance).GetValue($read.Invoke($null,@([string]$file))) -ceq '') 'Cleared draft was resurrected'
[IO.File]::Copy($file,$file+'.bak',$true)
[IO.File]::WriteAllText($file,'{broken')
Assert ($read.Invoke($null,@([string]$file)).Collection -eq 'Design') 'Damaged primary file did not recover'

$pane = [Activator]::CreateInstance((TypeOf ChatPane),@($chat.PSObject.BaseObject))
try {
    $inputBox = $pane.GetType().GetField('input',$instance).GetValue($pane)
    $null = $inputBox.Handle
    $inputBox.GetType().GetMethod('OnGotFocus',$instance).Invoke($inputBox,@([EventArgs]::Empty))
    Assert (!$inputBox.Controls[0].Visible) 'Empty composer hint covered the caret on focus'
    $inputBox.Text = 'UI draft'
    Assert ($chat.GetType().GetField('PendingInput',$instance).GetValue($chat) -eq 'UI draft') 'Typing did not update the chat draft'
} finally { $pane.Dispose() }

$ws = NewRecord TermWorkspace
$ws.Name = 'Pair'; $ws.Mode = 'panes'; $ws.Layout = '2'
foreach ($shell in @('pwsh','cmd')) {
    $p = NewRecord TermPane; $p.Shell=$shell; $p.Command='echo '+$shell; $p.Folder=$fixture; $p.Name=$shell; $ws.Panes.Add($p)
}
$history = TypeOf TermHistory
$history.GetMethod('Add',$static).Invoke($null,@('workspace','Pair',[string[]]@($fixture,$fixture),'echo pwsh | echo cmd','pwsh','2',$ws))
$entry = $history.GetProperty('All',$static).GetValue($null,$null)[0]
$ws.Panes[1].Command='changed'
$replay = $history.GetMethod('Restore',$static).Invoke($null,@($entry))
Assert ($replay.Mode -eq 'panes' -and $replay.Panes[0].Command -eq 'echo pwsh' -and $replay.Panes[1].Command -eq 'echo cmd' -and $replay.Panes[1].Shell -eq 'cmd') 'History lost mixed pane configuration'
$entry.Snapshot = $null
$legacy = $history.GetMethod('Restore',$static).Invoke($null,@($entry))
Assert ($legacy.Panes[0].Command -eq '' -and $legacy.Panes.Count -eq 2) 'Legacy summary reused as separate commands'

$files = TypeOf ChatFiles
$write = $files.GetMethod('Write',$static)
$attachment = Join-Path $fixture 'input.md'
[IO.File]::WriteAllText($attachment,'Unicode 東京')
$attached = $files.GetMethod('ReadAttachment',$static).Invoke($null,@([string]$attachment))
Assert ($attached.Contains('input.md') -and $attached.Contains('Unicode 東京')) 'File insertion changed text'
$fence = [string][char]96 * 3
$content = "| City | Amount | ID |`n| --- | --- | --- |`n| Cairo | 12.50 | 001 |`n| 東京 | 0 | =1+1 |"
$split = (TypeOf ChatView).GetMethod('Split',$static)
$card = $split.Invoke($null,@($fence+"file:Budget.xlsx`n"+$content+"`n"+$fence))
Assert ($card[0].FileName -ceq 'Budget.xlsx' -and $card[0].Text -ceq $content) 'File card content changed'
Assert (!$split.Invoke($null,@($fence+"file:Budget.xlsx`n"+$content))[0].FileName) 'Incomplete card marked complete'
$outer = $fence + [char]96
$nested = $fence + "cs" + [char]10 + "var item = 1;" + [char]10 + $fence
$card = $split.Invoke($null,@($outer + 'file:example.md' + [char]10 + $nested + [char]10 + $outer))
Assert ($card.Count -eq 1 -and $card[0].FileName -eq 'example.md' -and $card[0].Text -ceq $nested) 'Inner Markdown fence truncated the file'
foreach ($name in @('..\bad.xlsx','CON.pdf','bad.ps1','a:b.docx')) { Assert (!$files.GetMethod('IsName',$static).Invoke($null,@($name))) 'Unsafe card filename accepted' }
function XmlPart($archive,$name) {
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try { $reader = [IO.StreamReader]::new($zip.GetEntry($name).Open())
        try { [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
}
foreach ($ext in @('xlsx','docx','pptx','pdf','md','txt','csv')) {
    $sample = if ($ext -eq 'pptx') {"# One`nFirst body`n# Two`nSecond body"} elseif ($ext -eq 'pdf') { ("مرحبا Cairo — 東京`n`n" * 180) } else {$content}
    $write.Invoke($null,@([string](Join-Path $fixture ('sample.'+$ext)),('.'+$ext),[string]$sample))
}
$x = XmlPart (Join-Path $fixture sample.xlsx) 'xl/worksheets/sheet1.xml'
Assert ($x.worksheet.sheetData.row[1].c[1].v -eq '12.50' -and $x.worksheet.sheetData.row[1].c[2].is.t.InnerText -eq '001') 'Excel cell types changed'
Assert ($x.worksheet.sheetData.row[2].c[2].t -eq 'inlineStr') 'Formula-like chat text became executable'
$w = XmlPart (Join-Path $fixture sample.docx) 'word/document.xml'
Assert ($w.GetElementsByTagName('w:tr').Count -eq 3) 'Word table missing'
$slides = XmlPart (Join-Path $fixture sample.pptx) 'ppt/slides/slide2.xml'
Assert (($slides.GetElementsByTagName('a:t') | ForEach-Object { $_.InnerText }) -contains 'Two' -and ($slides.GetElementsByTagName('a:t') | ForEach-Object { $_.InnerText }) -contains 'Second body') 'Second slide missing'
$pdf = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $fixture sample.pdf)))
Assert ($pdf.StartsWith('%PDF-1.4') -and $pdf -match '/Count ([2-9]|\d\d+) ' -and $pdf.Contains('startxref')) 'PDF pagination failed'
Assert ([IO.File]::ReadAllText((Join-Path $fixture sample.md)) -ceq $content) 'Markdown changed'
Assert ([IO.File]::ReadAllText((Join-Path $fixture sample.txt)) -ceq $content) 'Plain text changed'
Assert ([IO.File]::ReadAllText((Join-Path $fixture sample.csv)).Contains('"Cairo","12.50","001"')) 'CSV cells missing'

# Files button review (2026-11-18): file mode only on an explicit request, safe CSV/XML, short bubbles, clear requests.
$docPrompt = $store.GetMethod('DocumentPrompt',$static)
function AskFile($text) {
    $c = NewRecord AgentChat; $m = NewRecord AgentMsg; $m.Role = 'user'; $m.Text = $text; $c.Messages.Add($m)
    ([string]$docPrompt.Invoke($null,@($c,'base'))) -ne 'base'
}
foreach ($t in @('Summarize this pdf for me','notes.md' + "`r`n`r`n" + 'meeting text','What is in README.md?')) { Assert (!(AskFile $t)) ('File mode triggered by a mention: ' + $t) }
foreach ($ext in @('md','xlsx','docx','pptx','pdf','txt','csv')) {
    $req = [string]$files.GetMethod('Request',$static).Invoke($null,@($ext))
    Assert (AskFile ($req + ' the sales table')) ('Files menu request not recognized: ' + $req)
}
Assert (([string]$files.GetMethod('Request',$static).Invoke($null,@('xlsx'))).StartsWith('Create an Excel')) 'Request article wrong'
Assert (AskFile 'Please export the result as an Excel workbook') 'Explicit export not recognized'
$write.Invoke($null,@([string](Join-Path $fixture 'inject.csv'),'.csv',"| A | B |`n| --- | --- |`n| =1+1 | -5 |`n| @SUM(1) | x |"))
$csvText = [IO.File]::ReadAllText((Join-Path $fixture 'inject.csv'))
Assert ($csvText.Contains('"''=1+1","-5"') -and $csvText.Contains('"''@SUM(1)"')) 'CSV formula cells not neutralized'
$control = "| A |`n| --- |`n| bad" + [char]11 + "char 東京 😀 |"
foreach ($ext in @('xlsx','docx','pptx')) { $write.Invoke($null,@([string](Join-Path $fixture ('control.'+$ext)),('.'+$ext),[string]$control)) }
Assert ((XmlPart (Join-Path $fixture control.xlsx) 'xl/worksheets/sheet1.xml').OuterXml.Contains('badchar 東京 😀')) 'Control character not removed or emoji lost'
$bubble = [string](TypeOf ChatView).GetMethod('Bubble',$static).Invoke($null,@([string]('x' * 200000)))
Assert ($bubble.Length -lt 3300 -and $bubble.Contains('197,000 more characters')) 'Long user bubble not shortened'
Assert (([string]$files.GetMethod('Describe',$static).Invoke($null,@('Budget.xlsx',"a`nb`nc"))) -eq 'Excel workbook, 3 lines') 'Save card description wrong'

Write-Host "SAVED WORK OK. Verified seven file formats, draft recovery, and terminal replay. Fixture $fixture"
