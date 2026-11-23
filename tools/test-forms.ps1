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
    $ui=$assembly.GetType('StickyPro.Ui')
    foreach ($dark in @($false,$true)) {
        $ui.GetField('Dark',$static).SetValue($null,$dark)
        $sample=[Activator]::CreateInstance($pageType,$instance,$null,[object[]]@($null),$null)
        try {
            $sample.Size=New-Object Drawing.Size(1800,850)
            $sample.CreateControl(); $sample.PerformLayout()
            foreach ($field in @('tbTitle','cbCat','tbTrigger','tbTags','tbBody')) {
                $inputControl=$pageType.GetField($field,$instance).GetValue($sample)
                $frame=$inputControl.Parent
                Assert ($frame.GetType() -eq [Windows.Forms.Panel] -and $inputControl.Width -gt 50 -and $inputControl.Height -ge $inputControl.Font.Height) "Prompt field clipped: $field"
                $bitmap=[Drawing.Bitmap]::new($frame.Width,$frame.Height)
                try {
                    $frame.DrawToBitmap($bitmap,$frame.ClientRectangle)
                    Assert ($bitmap.GetPixel(0,0).ToArgb() -ne $frame.BackColor.ToArgb()) "Prompt field border invisible: $field"
                } finally { $bitmap.Dispose() }
            }
            $cat=$pageType.GetField('cbCat',$instance).GetValue($sample)
            Assert ($cat.DropDownStyle -eq 'DropDown') 'Prompt categories are no longer editable'
        } finally { $sample.Dispose(); $ui.GetField('Dark',$static).SetValue($null,$false) }
    }
} finally { $event.RemoveEventHandler($null, $handler); $page.Dispose() }

$taskPage = [Activator]::CreateInstance($assembly.GetType('StickyPro.TasksPage'))
try {
    $type = $taskPage.GetType()
    $cards = $type.GetField('cards', $instance).GetValue($taskPage)
    $detail = $type.GetField('detail', $instance).GetValue($taskPage)
    $multi = $type.GetField('multi', $instance).GetValue($taskPage)
    $taskPage.Size = [Drawing.Size]::new(680, 570)
    $taskPage.Visible = $true
    $taskPage.CreateControl()
    Assert (-not $detail.Visible) 'An empty task detail pane takes space away from the task list.'
    foreach ($title in @('First task', 'Second task')) {
        $entry = [Activator]::CreateInstance($assembly.GetType('StickyPro.TaskItem'))
        $entry.Title = $title
        [void]$cards.Items.Add($entry)
    }
    $cards.SetSelected(0, $true)
    Assert ($detail.Visible) 'Selecting a task did not open its detail.'
    $cards.SetSelected(1, $true)
    Assert ($multi.Visible -and $multi.Enabled) 'Multi-selection task actions are unavailable.'
    $cards.ClearSelected()
    Assert (-not $detail.Visible) 'Clearing the selection did not release the task list space.'
} finally { $taskPage.Dispose() }

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
$settingsType = $assembly.GetType('StickyPro.SettingsForm')
$ui = $assembly.GetType('StickyPro.Ui')
$preferences = $store.GetField('Settings', $static).GetValue($null)
$preferences.Design = 'nexus'
[void]$ui.GetMethod('InitFonts', [type[]]@([string], [single])).Invoke($null, @('Calibri', [single]10))
foreach ($scale in @([single]1, [single]1.5)) {
    $ui.GetField('Scale', $static).SetValue($null, $scale)
    $settings = [Activator]::CreateInstance($settingsType, $instance, $null, [object[]]@($false), $null)
    try {
        $settings.Width = $settings.MinimumSize.Width
        $settings.PerformLayout()
        $table = $settings.Controls | Where-Object { $_ -is [Windows.Forms.TableLayoutPanel] } | Select-Object -First 1
        $table.PerformLayout()
        $jump = $settings.Controls | ForEach-Object { $_.Controls } | Where-Object { $_.AccessibleName -eq 'Settings section' }
        Assert ($jump.Items.Count -eq 8 -and $jump.Items[7] -eq 'Shortcuts') "Settings section navigation is incomplete at scale $scale."
        $awake = $settingsType.GetField('keepAwake', $instance).GetValue($settings)
        $checkSize = $awake.GetPreferredSize([Drawing.Size]::new($awake.Width, 0))
        Assert ($awake.Right -le $table.ClientSize.Width -and $awake.Height -ge $checkSize.Height) "Awake text clips at scale $scale."
        $theme = $settingsType.GetField('colorTheme', $instance).GetValue($settings)
        Assert ($theme.SelectedIndex -eq $preferences.Theme -and $theme.ItemHeight -ge $theme.Font.Height) "Color theme selection or row height changed at scale $scale."
    } finally { $settings.Dispose() }
}

$searchType = $assembly.GetType('StickyPro.GlobalSearch')
$find = $searchType.GetMethod('Find', $static)
$keep = $searchType.GetMethod('KeepResult', $static)
$listType = [Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType($searchType.GetNestedType('Hit', $static -bor $instance))
function Search-Hits([string]$text) {
    $result = [Activator]::CreateInstance($listType)
    foreach ($candidate in $find.Invoke($null, @($null, $text))) {
        if ($null -ne $candidate) { [void]$keep.Invoke($null, @($result, $candidate)) }
    }
    $result.ToArray()
}
$tasks = $store.GetField('Tasks', $static).GetValue($null)
foreach ($entry in @(@('Team follow-up', 'Review budget'), @('Review budget draft', ''), @('Review budget', ''))) {
    $task = [Activator]::CreateInstance($assembly.GetType('StickyPro.TaskItem'))
    $task.Title = $entry[0]; $task.Notes = $entry[1]; $tasks.Add($task)
}
$clips = $store.GetField('Clips', $static).GetValue($null)
for ($i = 0; $i -lt 25; $i++) {
    $clip = [Activator]::CreateInstance($assembly.GetType('StickyPro.Clip'))
    $clip.Text = 'Budget review, item ' + $i; $clip.Time = [datetime]::Today.AddMinutes($i).Ticks; $clips.Add($clip)
}
$hits = @(Search-Hits 'rEVIEW   Budget')
Assert ($hits.Count -eq 9) 'Search missed a group or failed to cap the clipboard history.'
Assert (($hits[0..2].Title -join '|') -eq 'Review budget|Review budget draft|Team follow-up') 'Search did not prioritize exact and prefix titles ahead of body text.'
Assert ($hits[3].Title -eq 'Budget review, item 24' -and $hits[8].Title -eq 'Budget review, item 19') 'Clipboard results were not grouped last and ordered by recency.'
Assert (@(Search-Hits 'Settings')[0].Group.ToString() -eq 'Page') 'Settings is missing from app search.'
Assert (@(Search-Hits 'unrelated PC filename').Count -eq 0) 'Unrelated query returned an app result.'
Assert (@(Search-Hits '  ').Count -eq 0) 'Blank search returned data.'
Write-Host 'FORMS OK: prompt duplication, card expansion, Settings layout and app search ranking and limits; no windows shown'
