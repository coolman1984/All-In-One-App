# Offscreen/managed checks only: no application entry point, clipboard writes,
# screen capture, Store.Load, user files, registry or visible windows.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Web.Extensions
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$instance = [Reflection.BindingFlags]'Public,NonPublic,Instance'
function Assert($value, [string]$message) { if (-not $value) { throw $message } }
$settingsType = $assembly.GetType('StickyPro.Settings')
$mode = $settingsType.GetField('SnipCopyMode', $instance)
Assert ($null -ne $mode) 'Snip copy preference is missing.'
$json = New-Object Web.Script.Serialization.JavaScriptSerializer
$settings = $json.Deserialize('{"SnipAutoCopy":false}', $settingsType)
Assert ($mode.GetValue($settings) -eq 'image') 'Old settings must default to Image.'
Assert (-not $settings.SnipAutoCopy) 'Old auto-copy setting was lost.'
$fixture = Join-Path $root ('build-test\snip-output-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$settings.SnipFolder = $fixture
$store = $assembly.GetType('StickyPro.Store')
$store.GetField('Settings', $static).SetValue($null, $settings)
$store.GetField('Dir', $static).SetValue($null, $fixture)
$bitmap = New-Object Drawing.Bitmap(32, 32)
$shot = $assembly.GetType('StickyPro.Shot')
foreach ($choice in @('image', 'path', 'both')) {
    $settings.SnipCopyMode = $choice; $settings.SnipAutoSave = $false
    $mustSave = $shot.GetMethod('NeedsSavedFile', $static).Invoke($null, @($settings))
    Assert ($mustSave -eq ($choice -ne 'image')) ('Wrong forced-save policy: ' + $choice)
    $arguments = [object[]]@($bitmap.PSObject.BaseObject, (Join-Path $fixture 'example.png'), $choice)
    for ($i = 0; $i -lt $arguments.Count; $i++) { $arguments[$i] = $arguments[$i].PSObject.BaseObject }
    $payload = $shot.GetMethod('SnipData', $static).Invoke($null, $arguments)
    Assert ($payload.GetDataPresent([Windows.Forms.DataFormats]::Bitmap) -eq ($choice -ne 'path')) ('Wrong bitmap format: ' + $choice)
    Assert ($payload.GetDataPresent([Windows.Forms.DataFormats]::UnicodeText) -eq ($choice -ne 'image')) ('Wrong path format: ' + $choice)
    Assert ($payload.GetDataPresent([Windows.Forms.DataFormats]::FileDrop) -eq ($choice -eq 'both')) ('Wrong file-drop format: ' + $choice)
}
$settings.SnipCopyMode = 'image'
$editorType = $assembly.GetType('StickyPro.SnipEditor')
$editor = [Activator]::CreateInstance($editorType, $instance, $null, [object[]]@($bitmap.PSObject.BaseObject, $null), $null)
try {
    Assert ($editorType.GetField('tool', $instance).GetValue($editor) -eq 'rect') 'New editor did not select Box.'
    $tools = $editorType.GetField('tools', $instance).GetValue($editor)
    Assert (@($tools.Items | Where-Object { $_.Text -eq 'Copy path' }).Count -eq 1) 'Editor Copy path action is missing.'
    Assert (-not $editor.Visible) 'Editor test unexpectedly displayed a window.'
} finally { $editor.Dispose(); $bitmap.Dispose() }
$panelType = $assembly.GetType('StickyPro.SnipPanel')
$panel = [Activator]::CreateInstance($panelType, $instance, $null, [object[]]@($null), $null)
try {
    $list = $panelType.GetField('list', $instance).GetValue($panel)
    Assert ($list.SelectionMode -eq [Windows.Forms.SelectionMode]::MultiExtended) 'Recent snips do not support multi-selection.'
    Assert (@($list.ContextMenuStrip.Items | Where-Object { $_.Text -eq 'Copy as path' }).Count -eq 1) 'Recent-snips Copy as path action is missing.'
    $menus = @{}
    foreach ($group in $panel.Controls) {
        foreach ($action in $group.Controls) {
            if ($action -is [Windows.Forms.Button] -and $action.ContextMenuStrip) {
                $menus[$action.AccessibleName] = @($action.ContextMenuStrip.Items | Where-Object { $_ -is [Windows.Forms.ToolStripMenuItem] } | ForEach-Object { $_.Text })
            }
        }
    }
    foreach ($label in 'Window', 'Full screen', 'Fixed region', 'Scrolling window', 'Window control', 'Free-form', 'Repeat last capture') {
        Assert ($label -in $menus['Capture mode']) "Capture menu lost $label."
    }
    Assert ($menus['Record'].Count -eq 3 -and $menus['Files'].Count -eq 2) 'Grouped capture actions are missing.'
} finally { $panel.Dispose() }
Write-Host 'SNIP OUTPUT OK: backward-compatible Image default, Box selected, editor and multi-select path actions'
