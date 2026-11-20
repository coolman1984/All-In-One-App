# Render actual Explorer list handles against synthetic folders, without opening the app.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$root = Split-Path -Parent $PSScriptRoot
$asm = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$instance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$fixture = Join-Path $root ('build-test\paint-' + [guid]::NewGuid().ToString('N'))
$folder = [IO.Directory]::CreateDirectory((Join-Path $fixture 'listing')).FullName
$other = [IO.Directory]::CreateDirectory((Join-Path $fixture 'other')).FullName
$asm.GetType('StickyPro.Store').GetField('Dir',$static).SetValue($null,$fixture)
$type = $asm.GetType('StickyPro.FolderPane')
$listField = $type.GetField('lv',$instance)
$panes = @()

function Wait-ForRows($pane, $count) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        [Windows.Forms.Application]::DoEvents()
        if ($listField.GetValue($pane).VirtualListSize -eq $count -and !$type.GetField('loading',$instance).GetValue($pane)) { return }
        Start-Sleep -Milliseconds 10
    } while ($timer.ElapsedMilliseconds -lt 5000)
    throw "Explorer did not publish $count rows"
}

function Assert-Painted($list, $stage) {
    $bitmap = New-Object Drawing.Bitmap($list.Width,$list.Height)
    try {
        $list.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$list.Width,$list.Height)))
        if ($null -eq $list.TopItem) { throw "$stage has no visible top item in a populated pane" }
        $top = $list.TopItem.Index
        $checked = 0
        for ($i = $top; $i -lt $list.VirtualListSize; $i++) {
            $bounds = $list.GetItemRect($i)
            if ($bounds.Bottom -gt $list.ClientSize.Height) { break }
            if ($bounds.Top -lt 0) { continue }
            # Look inside the name, past the icon and away from focus borders.
            $ink = 0
            for ($y = $bounds.Top + 2; $y -lt $bounds.Bottom - 2; $y++) {
                $bg = $bitmap.GetPixel(165,$y)
                for ($x = 28; $x -lt 133; $x += 2) {
                    $c = $bitmap.GetPixel($x,$y)
                    if ([Math]::Abs($bg.R - $c.R) + [Math]::Abs($bg.G - $c.G) + [Math]::Abs($bg.B - $c.B) -gt 140) { $ink++ }
                }
            }
            if ($ink -lt 9) { throw "$stage rendered a blank name at row $i" }
            $checked++
        }
        if ($checked -lt 3) { throw "$stage checked too few visible rows ($checked)" }
    } finally { $bitmap.Dispose() }
}

try {
    foreach ($i in 1..80) {
        [IO.File]::WriteAllText((Join-Path $folder ('document-{0:D3}.txt' -f $i)),'')
        if ($i -le 4) { [void][IO.Directory]::CreateDirectory((Join-Path $other "directory-$i")) }
    }
    foreach ($location in @($folder,$other,$folder)) {
        $pane = [Activator]::CreateInstance($type,$true)
        $panes += $pane
        $pane.Size = New-Object Drawing.Size(620,360)
        $pane.CreateControl()
        $pane.Navigate($location,$false)
    }
    for ($i = 0; $i -lt 3; $i++) {
        $count = if ($i -eq 1) {4} else {80}
        Wait-ForRows $panes[$i] $count
        Assert-Painted ($listField.GetValue($panes[$i])) "Pane $i"
    }
    $first = $listField.GetValue($panes[0])
    $first.EnsureVisible(60)
    [void]$first.SelectedIndices.Add(57)
    $first.Update()
    Assert-Painted $first 'Scrolled and selected'
    $anchor = $first.TopItem.Index
    [IO.File]::WriteAllText((Join-Path $folder 'new-file.txt'),'')
    $type.GetMethod('Load',$instance,$null,[type[]]@([bool],[string]),$null).Invoke($panes[0],@($false,$null))
    Wait-ForRows $panes[0] 81
    if ($first.TopItem.Index -ne $anchor) { throw 'Refreshing a folder jumped away from the visible rows' }
    Assert-Painted $first 'Refreshed while scrolled'
    $filter = $type.GetField('filter',$instance).GetValue($panes[0])
    $filter.Text = 'document-01'
    Wait-ForRows $panes[0] 10
    Assert-Painted $first 'Filtered'
    $filter.Text = 'document-02'
    $type.GetMethod('Apply',$instance).Invoke($panes[0],@())
    if ($first.Items[0].Text -notlike 'document-02*') { throw 'Equal-size replacement kept the old names' }
    Assert-Painted $first 'Equal-size replacement'
    $filter.Clear()
    Wait-ForRows $panes[0] 81
    $first.EnsureVisible(70)
    $panes[0].Navigate($other,$false)
    Wait-ForRows $panes[0] 4
    Assert-Painted $first 'Navigated to a smaller folder'
    Assert-Painted ($listField.GetValue($panes[2])) 'Sibling after filtering'
} finally {
    foreach ($pane in $panes) { $pane.Dispose() }
    $resolved = (Resolve-Path -LiteralPath $fixture).Path
    if ([IO.Path]::GetDirectoryName($resolved) -eq (Join-Path $root 'build-test') -and [IO.Path]::GetFileName($resolved) -match '^paint-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
Write-Host 'EXPLORER PAINT OK: three panes, scrolled refresh, filtering and folder navigation'
