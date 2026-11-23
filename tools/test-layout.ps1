# Construct hidden controls only. Never Show, Program.Main, Store.Load, or use
# production data. Native list columns may scroll; action controls may not clip.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$instance = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$store = $assembly.GetType('StickyPro.Store')
$fixture = Join-Path $root ('build-test\layout-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$store.GetField('Dir', $static).SetValue($null, $fixture)
$store.GetField('ImgDir', $static).SetValue($null, $fixture)
$settings = $store.GetField('Settings', $static).GetValue($null)
$settings.SnipFolder = $fixture
$category = [Activator]::CreateInstance($assembly.GetType('StickyPro.TagCategory'))
$category.Name = 'Urgent'; $category.Color = 'Red'
$store.GetField('TagCats', $static).GetValue($null).Add($category)
$getState = [Windows.Forms.Control].GetMethod('GetState', $instance)
# WinForms caps sizable windows at the desktop's native tracking width, even when hidden.
$maxWidth = [Windows.Forms.SystemInformation]::MaxWindowTrackSize.Width
$widths = @(700, 1000, 1600 | Where-Object { $_ -le $maxWidth })
if (!$widths.Count) { throw "The desktop is too narrow for the layout fixture (maximum tracking width $maxWidth px)." }
if ($widths[-1] -lt 1600 -and $maxWidth -gt $widths[-1]) { $widths += $maxWidth }
Write-Host ("Testing widths " + ($widths -join '/') + " px (native tracking maximum $maxWidth px)")
$script:checks = 0
function Layout-All($control) {
    $control.PerformLayout()
    foreach ($child in $control.Controls) { Layout-All $child }
    $control.PerformLayout()
}
function Check-Actions($control, [string]$path) {
    foreach ($child in $control.Controls) {
        if ($null -ne $getState -and -not $getState.Invoke($child, @([int]2))) { continue }
        if ($child -is [Windows.Forms.Button] -or $child -is [Windows.Forms.ComboBox] -or $child -is [Windows.Forms.CheckBox] -or $child -is [Windows.Forms.TextBox]) {
            if ($control -isnot [Windows.Forms.ToolStrip] -and $child.Right -gt $control.ClientSize.Width + 2) { throw ("Clipped action at " + $path + ': ' + $child.Text + ' right=' + $child.Right + ' parent=' + $control.ClientSize.Width) }
            $script:checks++
        }
        if ($child -isnot [Windows.Forms.ToolStrip]) { Check-Actions $child ($path + '/' + $child.GetType().Name) }
    }
}
$main = [Activator]::CreateInstance($assembly.GetType('StickyPro.MainForm'), $true)
try {
    foreach ($width in $widths) {
        $main.MaximumSize = [Drawing.Size]::Empty
        $main.Size = New-Object Drawing.Size($width, 900)
        if ($main.Width -ne $width) { throw "Main window cannot reflow at $width px." }
        foreach ($page in @('dashboard', 'tasks', 'notes', 'prompts', 'clips', 'system', 'cards', 'snip', 'activity', 'tagged', 'shortcuts')) {
            $main.ShowPage($page)
            Layout-All $main
            Check-Actions $main ("Main/$page/$width")
        }
        $shortcuts = $main.GetType().GetField('loadedPages', $instance).GetValue($main)['shortcuts']
        $tabBar = $shortcuts.GetType().GetField('tabs', $instance).GetValue($shortcuts)
        foreach ($tab in $tabBar.Controls) {
            if (-not $tab.TabStop -or $tab.Right -gt $tabBar.ClientSize.Width + 2 -or $tab.Bottom -gt $tabBar.ClientSize.Height + 2) {
                throw "Shortcut tab is inaccessible or clipped at ${width}px: $($tab.Text)."
            }
        }
    }
    if ($main.Visible) { throw 'Layout test displayed a window.' }
} finally { $main.Exiting = $true; $main.Dispose() }
$settingsForm = [Activator]::CreateInstance($assembly.GetType('StickyPro.SettingsForm'), $instance, $null, [object[]]@($false), $null)
try {
    $settingsForm.MaximumSize = [Drawing.Size]::Empty
    foreach ($width in $widths) { $settingsForm.Width = $width; Layout-All $settingsForm; Check-Actions $settingsForm ("Settings/$width") }
} finally { $settingsForm.Dispose() }
$bitmap = New-Object Drawing.Bitmap(32, 32)
$editor = [Activator]::CreateInstance($assembly.GetType('StickyPro.SnipEditor'), $instance, $null, [object[]]@($bitmap.PSObject.BaseObject, $null), $null)
try {
    foreach ($width in $widths) { $editor.Width = $width; Layout-All $editor; Check-Actions $editor ("Editor/$width") }
} finally { $editor.Dispose(); $bitmap.Dispose() }
Write-Host ("LAYOUT OK: " + ($widths -join "/") + " px; " + $script:checks + ' action bounds checked; no windows shown')
