# Synthetic data and hidden WinForms controls only. The live app and user data are never opened.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$instance = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$store = $assembly.GetType('StickyPro.Store')
$fixture = Join-Path $root ('build-test\responsiveness-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$store.GetField('Dir', $static).SetValue($null, $fixture)
$store.GetField('ImgDir', $static).SetValue($null, $fixture)

function Assert($condition, [string]$message) { if (-not $condition) { throw $message } }
function Wait-For([scriptblock]$condition, [string]$message) {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    do {
        [Windows.Forms.Application]::DoEvents()
        if (& $condition) { return }
        Start-Sleep -Milliseconds 10
    } while ($deadline.ElapsedMilliseconds -lt 5000)
    throw $message
}

$list = [Activator]::CreateInstance($assembly.GetType('StickyPro.CardList'))
try {
    $list.SelectionMode = 'MultiExtended'
    $null = $list.Handle
    $list.SetItems([object[]](1..20000))
    $script:selectionEvents = 0
    $list.add_SelectedIndexChanged({ $script:selectionEvents++ })
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $list.SelectAll()
    $selectTime = $timer.ElapsedMilliseconds
    Assert ($list.SelectedItems.Count -eq 20000 -and $script:selectionEvents -eq 1) 'Select All did not notify consumers only once.'
    Assert ($selectTime -lt 2500) 'Select All returned to a slow per-item path.'
    $selected = $list.GetType().GetMethod('Selected').MakeGenericMethod([int]).Invoke($list, @())
    Assert ($selected.Count -eq 20000 -and $selected[0] -eq 1 -and $selected[19999] -eq 20000) 'Bulk selection is missing rows from copy operations.'
    $list.SetItems([object[]](1..10000))
    Assert ($list.SelectedItems.Count -eq 10000 -and $script:selectionEvents -eq 2) 'Filtering lost the retained selection or emitted per-item events.'
} finally { $list.Dispose() }

$clipType = $assembly.GetType('StickyPro.Clip')
$clips = [Array]::CreateInstance($clipType, 1000)
$clip = [Activator]::CreateInstance($clipType)
$clip.Text = 'x' * 2000000
for ($i = 0; $i -lt $clips.Length; $i++) { $clips.SetValue($clip, $i) }
$preview = $assembly.GetType('StickyPro.ClipsPage').GetMethod('PreviewText', $static).Invoke($null, (,[object]$clips))
Assert ($preview.Length -le 32003 -and $preview.EndsWith([string][char]0x2026) -and $clip.Text.Length -eq 2000000) 'Preview exceeded its size limit or altered the stored clip.'

$first = Join-Path $fixture 'first'; $second = Join-Path $fixture 'second'
[void][IO.Directory]::CreateDirectory($first); [void][IO.Directory]::CreateDirectory($second)
$noteA = Join-Path $first 'first.md'; $noteB = Join-Path $second 'second.md'
[IO.File]::WriteAllText($noteA, 'First note'); [IO.File]::WriteAllText($noteB, 'Second note')
$pane = [Activator]::CreateInstance($assembly.GetType('StickyPro.FolderPane'), $true)
try {
    $pane.Navigate($first, $false)
    $pane.Navigate($second, $false)
    Assert ($pane.Current -ne $second) 'Explorer navigation ran synchronously.'
    $paneType = $pane.GetType()
    $activeLoad = $paneType.GetField('loading', $instance)
    Wait-For { $pane.Current -eq $second -and -not $activeLoad.GetValue($pane) } 'The latest Explorer navigation did not win.'
    $rows = $paneType.GetField('rows', $instance).GetValue($pane)
    Assert ($rows.Count -eq 1 -and $rows[0].Path -eq $noteB) 'Explorer published a stale directory listing.'
    $pane.Navigate((Join-Path $fixture 'missing'), $false)
    $foot = $paneType.GetField('foot', $instance).GetValue($pane)
    Wait-For { $foot.Text.StartsWith('Folder unavailable') } 'Explorer failed to report a missing folder.'
    Assert ($pane.Current -eq $second) 'A failed navigation replaced the current folder.'
} finally { $pane.Dispose() }

$vault = $assembly.GetType('StickyPro.MdVault')
$settings = $store.GetField('Settings', $static).GetValue($null)
$settings.MdVaults.Add($first); $settings.MdVaults.Add($second)
$vault.GetMethod('Index', $static).Invoke($null, @($true))
$settings.MdVaultIndex = 1
$vault.GetMethod('Index', $static).Invoke($null, @($true))
$busy = $vault.GetProperty('Busy', $static)
Wait-For { -not $busy.GetValue($null, $null) } 'Markdown indexing did not finish.'
$paths = @($vault.GetMethod('SearchPaths', $static).Invoke($null, @()))
Assert ($paths.Count -eq 1 -and $paths[0] -eq $noteB) 'Markdown indexed a superseded vault.'
$settings.MdMode = 'split'
$main = [Activator]::CreateInstance($assembly.GetType('StickyPro.MainForm'), $true)
try {
    $main.ShowPage('markdown')
    $page = $main.GetType().GetField('markdown',$instance).GetValue($main)
    $null = $page.Handle
    $split = $page.GetType().GetField('split',$instance).GetValue($page)
    foreach ($width in @(1450, 480, 1000, 1600)) {
        $page.Width = $width
        $page.PerformLayout()
        $page.GetType().GetMethod('SetMode',$instance).Invoke($page,@('split'))
    }
    Assert ($settings.MdMode -eq 'split' -and !$split.Panel1Collapsed -and !$split.Panel2Collapsed) 'Saved Markdown Split mode was lost.'
    $page.Open($noteA, $false); $main.OpenMarkdownFile($noteB)
    $current = $page.GetType().GetField('current', $instance)
    Wait-For { $current.GetValue($page) -eq $noteB } 'A superseded Markdown note open won.'
    $editor = $page.GetType().GetField('editor', $instance).GetValue($page)
    Assert ($editor.Text -eq 'Second note') 'Markdown editor received stale text.'
} finally { $main.Exiting = $true; $main.Dispose() }
$settings.MdLast = $noteA
$standalone = [Activator]::CreateInstance($assembly.GetType('StickyPro.MarkdownPage'), $true)
try {
    $null = $standalone.Handle
    $standalone.Size = New-Object Drawing.Size(1350,700)
    $standalone.Visible = $true
    Wait-For { $current.GetValue($standalone) -eq $noteA } 'Showing Markdown did not restore the saved note.'
    $tree = $standalone.GetType().GetField('tree',$instance).GetValue($standalone)
    $node = $tree.Nodes.Find('file',$true) | Where-Object Tag -eq $noteB | Select-Object -First 1
    Assert ($null -ne $node) 'Indexed note missing from the vault tree.'
    $tree.SelectedNode = $node
    $tree.GetType().GetMethod('OnKeyDown',$instance).Invoke($tree,@([Windows.Forms.KeyEventArgs]::new([Windows.Forms.Keys]::Enter)))
    Wait-For { $current.GetValue($standalone) -eq $noteB } 'Opening a note from the vault tree failed.'
} finally { $standalone.Dispose() }
$settings.MdVaults.Clear()
$vault.GetMethod('Index', $static).Invoke($null, @($false))
Assert (@($vault.GetMethod('SearchPaths', $static).Invoke($null, @())).Count -eq 0) 'Removing the active Markdown vault left old results available.'

$imagePath = Join-Path $fixture 'image.png'
$original = [Drawing.Bitmap]::new(640, 480)
try { $original.Save($imagePath, [Drawing.Imaging.ImageFormat]::Png) } finally { $original.Dispose() }
$image = $assembly.GetType('StickyPro.Thumbs').GetMethod('LoadScaled', $static).Invoke($null, @([string]$imagePath, 96, 96))
try { Assert ($null -ne $image -and $image.Width -eq 96 -and $image.Height -eq 72) 'Scaled image preview broke aspect ratio.' }
finally { if ($null -ne $image) { $image.Dispose() } }
$stream = [IO.File]::Open($imagePath, 'Open', 'Read', 'None')
$stream.Dispose()
Write-Host "RESPONSIVENESS OK: 20,000 selections in $selectTime ms with one notification, bounded preview, latest folder and note, scaled image"
