# Offscreen form construction and model regression checks only. No Show, Main,
# Store.Load, clipboard, Office, registry or application process is invoked.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $root 'build-test\StickyNotesPro.test.exe'))
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$instance = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$store = $assembly.GetType('StickyPro.Store')
$fixture = Join-Path $root ('build-test\forms-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$store.GetField('Dir', $static).SetValue($null, $fixture)
$store.GetField('ImgDir', $static).SetValue($null, $fixture)
function Assert($condition, [string]$message) { if (-not $condition) { throw $message } }

$prompts = $store.GetField('Prompts', $static).GetValue($null)
$original = [Activator]::CreateInstance($assembly.GetType('StickyPro.Prompt'))
$original.Id = 'original'; $original.Title = 'Original'; $original.Body = 'Saved prompt text'; $original.Tags = 'work, Arabic'
$prompts.Add($original)
$pageType = $assembly.GetType('StickyPro.PromptsPage')
$page = [Activator]::CreateInstance($pageType, $instance, $null, [object[]]@($null), $null)
$refresh = $pageType.GetMethod('RefreshAll', $instance)
$handler = [Action]{ [void]$refresh.Invoke($page, @()) }
$event = $store.GetEvent('PromptsChanged', $static)
try {
    [void]$refresh.Invoke($page, @())
    $list = $pageType.GetField('list', $instance).GetValue($page)
    $list.SelectedItem = $original
    $event.AddEventHandler($null, $handler)
    [void]$pageType.GetMethod('Duplicate', $instance).Invoke($page, @())
    Assert ($prompts.Count -eq 2) 'Duplicate did not create one prompt.'
    Assert ($prompts[0].Tags -eq $original.Tags -and $prompts[0].Body -eq $original.Body) 'Duplicate lost its original prompt after the refresh event.'
    $page.Width = 546
    $editor = $pageType.GetField('editor', $instance).GetValue($page)
    Assert ($editor.Width -ge 280) 'Narrow prompt layout crowded out its editor.'
    $picker = $pageType.GetField('compactCats', $instance).GetValue($page)
    Assert ($picker.Visible -and $picker.Items.Count -gt 0) 'Narrow prompt layout lost its category selector.'
} finally { $event.RemoveEventHandler($null, $handler); $page.Dispose() }

$definition = [Activator]::CreateInstance($assembly.GetType('StickyPro.CardDef'))
$definition.Id = 'card-fixture'; $definition.W = 600; $definition.H = 500; $definition.X = 100; $definition.Y = 100
$definition.Features.Add('notes'); $definition.Active = 'notes'
$card = [Activator]::CreateInstance($assembly.GetType('StickyPro.CardForm'), $instance, $null, [object[]]@($definition), $null)
try {
    $definition.Collapsed = $true
    $card.MinimumSize = [Drawing.Size]::Empty
    $card.Size = New-Object Drawing.Size(38, 34)
    $card.Expand()
    Assert ($card.Width -eq 600 -and $card.Height -eq 500) 'Expanding a card overwrote its saved dimensions.'
    Assert (-not $card.Visible) 'Offscreen card test unexpectedly displayed a window.'
    $features = New-Object 'Collections.Generic.List[string]'
    foreach ($feature in $assembly.GetType('StickyPro.Features').GetField('All', $static).GetValue($null)) { $features.Add($feature[0]) }
    $card.SetFeatures($features)
    $card.Width = 240; $card.Height = 160
    $card.PerformLayout()
    $body = $card.GetType().GetField('body', $instance).GetValue($card)
    Assert ($body.Height -gt 40) 'Wrapped card tabs left no space for their content.'
} finally { $card.Dispose() }
Write-Host 'FORMS OK: prompt duplication survives refresh; card expansion preserves dimensions; no windows shown'
