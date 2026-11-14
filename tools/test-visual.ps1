# Offscreen control rendering only. Does not launch the application or read data/.
# Run under Windows PowerShell 5.1 after tools/test-build.ps1.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$ui = $assembly.GetType('StickyPro.Ui')
function Token([string]$name) { $ui.GetProperty($name, $flags).GetValue($null, $null) }
function Button([string]$text, [bool]$primary) {
    $ui.GetMethod('Btn', $flags).Invoke($null, @($text, $null, $primary))
}

foreach ($dark in @($false, $true)) {
    $ui.GetField('Dark', $flags).SetValue($null, $dark)
    $panel = New-Object Windows.Forms.Panel
    $panel.Size = New-Object Drawing.Size(760, 420)
    $panel.BackColor = Token 'Bg'
    $panel.ForeColor = Token 'Text'
    $panel.Font = $ui.GetField('Base', $flags).GetValue($null)
    try {
        $title = New-Object Windows.Forms.Label
        $title.Text = 'Sticky Notes Pro'
        $title.Font = $ui.GetField('Big', $flags).GetValue($null)
        $title.SetBounds(24, 16, 700, 40)
        $panel.Controls.Add($title)
        $bar = $assembly.GetType('StickyPro.MainForm').GetMethod('Bar', $flags).Invoke($null, @())
        $toolbarHost = New-Object Windows.Forms.Panel
        $toolbarHost.SetBounds(24, 64, 360, 120)
        $bar.Dock = 'Top'
        foreach ($label in @('New note', 'Copy', 'Add to queue', 'Archive', 'Settings')) {
            $bar.Controls.Add((Button $label ($label -eq 'New note')))
        }
        $toolbarHost.Controls.Add($bar)
        $panel.Controls.Add($toolbarHost)
        $search = $ui.GetMethod('Search', $flags).Invoke($null, @('Search notes and tags'))
        $search.Text = 'Search notes and tags'
        $search.SetBounds(410, 76, 320, 30)
        $panel.Controls.Add($search)
        $combo = New-Object Windows.Forms.ComboBox
        $combo.DropDownStyle = 'DropDownList'
        [void]$combo.Items.Add('All notes')
        [void]$combo.Items.Add('Arabic / RTL')
        $combo.SelectedIndex = 0
        $combo.SetBounds(410, 116, 190, 28)
        $panel.Controls.Add($combo)
        $check = New-Object Windows.Forms.CheckBox
        $check.Text = 'Keep on top'
        $check.Checked = $true
        $check.SetBounds(410, 150, 220, 28)
        $panel.Controls.Add($check)
        $list = [Activator]::CreateInstance($assembly.GetType('StickyPro.FastList'), $true)
        $list.SetBounds(24, 190, 706, 200)
        [void]$list.Columns.Add('Feature', 210)
        [void]$list.Columns.Add('Status', 160)
        [void]$list.Columns.Add('Details', 310)
        $item = New-Object Windows.Forms.ListViewItem('Sticky notes')
        [void]$item.SubItems.Add('Ready')
        [void]$item.SubItems.Add('Rich text, reminders and Arabic RTL')
        [void]$list.Items.Add($item)
        $item = New-Object Windows.Forms.ListViewItem('Clipboard history')
        [void]$item.SubItems.Add('1,000 items')
        [void]$item.SubItems.Add('Text, images and files')
        [void]$list.Items.Add($item)
        $panel.Controls.Add($list)
        $ui.GetMethod('Theme', $flags).Invoke($null, @($panel.PSObject.BaseObject))
        $panel.CreateControl()
        $panel.PerformLayout()
        $toolbarHost.PerformLayout()
        $bar.PerformLayout()
        $tops = @($bar.Controls | ForEach-Object { $_.Top } | Select-Object -Unique)
        if (-not $bar.WrapContents -or $tops.Count -lt 2 -or $bar.Width -gt $toolbarHost.Width) { throw 'Narrow toolbar did not wrap within its host.' }
        $bitmap = New-Object Drawing.Bitmap(760, 420)
        try {
            $panel.DrawToBitmap($bitmap, (New-Object Drawing.Rectangle(0, 0, 760, 420)))
            if ($dark -and ($bitmap.GetPixel(500, 130).GetBrightness() -gt 0.5 -or $bitmap.GetPixel(725, 200).GetBrightness() -gt 0.5)) { throw 'Native combo face or trailing list header rendered white in dark mode.' }
            if ($combo.Text -ne 'All notes') { throw 'Combo painting changed the selected value.' }
            $name = if ($dark) { 'controls-dark.png' } else { 'controls-light.png' }
            $bitmap.Save((Join-Path $root ('build-test\' + $name)), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
        $panel.Scale((New-Object Drawing.SizeF(1.5, 1.5)))
        $panel.PerformLayout(); $toolbarHost.PerformLayout(); $bar.PerformLayout()
        if ($bar.Width -gt $toolbarHost.Width) { throw 'Scaled toolbar escaped its host.' }
        foreach ($button in $bar.Controls) { if ($button.Right -gt $bar.ClientSize.Width -or $button.Bottom -gt $bar.ClientSize.Height) { throw 'Scaled toolbar clipped an action.' } }
        $scaled = New-Object Drawing.Bitmap($panel.Width, $panel.Height)
        try {
            $panel.DrawToBitmap($scaled, (New-Object Drawing.Rectangle(0, 0, $panel.Width, $panel.Height)))
            $name = if ($dark) { 'controls-dark-150.png' } else { 'controls-light-150.png' }
            $scaled.Save((Join-Path $root ('build-test\' + $name)), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $scaled.Dispose() }
    } finally { $panel.Dispose() }
}
Write-Host 'VISUAL OK: light/dark controls, combo/check states, narrow wrapping, 150% layout scaling (offscreen)'
