# Full UI sweep and stress test. Loads the test build with a COPY of a data folder (never data\ itself), then for
# every look (design x light/dark) and window size: opens every page, captures a screenshot, checks the layout
# (controls cut off by their parent, button text that does not fit, empty pages), opens every right-click menu,
# and records every exception. Then stresses: fast page switching, resize sweeps, search typing.
# Output: <Out>\report.md, <Out>\sheet-*.png (contact sheets), <Out>\pages\*.png
param(
    [string]$Data = '',                      # folder to copy as the app data (default: empty fixture)
    [string]$Out = '',
    [string]$AssemblyPath = '',
    [switch]$SkipStress,
    [string[]]$Looks = @('soft-light', 'soft-dark', 'serq-light', 'nexus-dark', 'classic-light'),
    [string[]]$Sizes = @('1100x700', '1500x900', '1920x1080'),
    [switch]$Quick                            # one look, one size
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
# Errors go to this script (and the report), never to a dialog on the user's screen.
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SweepNative {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int value);
}
'@
$root = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $root ('build-test\sweep-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force (Join-Path $Out 'pages') | Out-Null
if ($Quick) { $Looks = @('soft-light'); $Sizes = @('1500x900') }

if (-not $AssemblyPath) {
    $runtime = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $runtime -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test-build.ps1') -Name 'UiSweep.test.exe' -OfflineUi
    if ($LASTEXITCODE -ne 0) { throw 'Offline UI build failed.' }
    $AssemblyPath = Join-Path $root 'build-test\UiSweep.test.exe'
}
$exe = Join-Path $Out 'app.exe'
Copy-Item -LiteralPath $AssemblyPath -Destination $exe -Force
foreach ($library in 'Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll') {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $root "lib\webview2\$library"))
}
$asm = [Reflection.Assembly]::LoadFrom($exe)
if ($asm.GetManifestResourceInfo('webview2.WebView2Loader.dll')) { throw 'Use an offline UI build. This sweep does not start the embedded browser.' }
$storeT = $asm.GetType('StickyPro.Store')
$fixture = Join-Path $Out 'data'
if ($Data) { robocopy $Data $fixture /E /NFL /NDL /NJH /NJS /XD agents browser backups /XF error.log perf.txt | Out-Null } else { New-Item -ItemType Directory -Force $fixture | Out-Null }
$storeT.GetField('DirOverride').SetValue($null, $fixture)
$storeT.GetMethod('Load', [Type[]]@()).Invoke($null, $null)
$st = $storeT.GetField('Settings').GetValue($null)
$st.BrowserTabs.Clear(); $st.BrowserAutomation = $false
$st.ViewerTabs.Clear()
$st.SnipFolder = Join-Path $fixture 'snips'
[void][IO.Directory]::CreateDirectory($st.SnipFolder)
$private = [Reflection.BindingFlags]'Instance,NonPublic'
$asm.GetType('StickyPro.AgentLimits').GetField('lastAt', [Reflection.BindingFlags]'Static,NonPublic').SetValue($null, [datetime]::MaxValue)
function Add-Record($list, [string]$type, [hashtable]$properties) {
    $item = [Activator]::CreateInstance($asm.GetType("StickyPro.$type", $true), $true)
    foreach ($key in $properties.Keys) { $item.$key = $properties[$key] }
    $list.Add($item)
    return $item
}
if (-not $Data) {
    $sample = Join-Path $fixture 'Sample project'
    [void][IO.Directory]::CreateDirectory($sample)
    $md = Join-Path $sample 'Release checklist.md'
    $md2 = Join-Path $sample 'Work notes.md'
    [IO.File]::WriteAllText($md, "# Release checklist`r`n`r`n## Verify`r`n`r`n- Run the tests`r`n- Review [[Work notes]]`r`n`r`n## Ship`r`n`r`n#release")
    [IO.File]::WriteAllText($md2, "# Work notes`r`n`r`nSee [[Release checklist]].")
    $st.ExplorerPaths.Clear(); $st.ExplorerPaths.Add($sample); $st.ExplorerPaths.Add($sample)
    $st.MdVaults.Clear(); $st.MdVaults.Add($sample); $st.MdLast = $md
    $now = [datetime]::Now.Ticks
    $records = @(
        @('Notes','Note',@{Id='sweep-note';Text="Release notes`r`nCheck the new build.";Modified=$now;Tags='work'}),
        @('Prompts','Prompt',@{Id='sweep-prompt';Title='Review release notes';Body='List the changes and verification for {{project}}.';Category='Writing';Tags='work';Modified=$now;Fav=$true}),
        @('Clips','Clip',@{Id='sweep-clip';Text='Fixture clipboard entry';Source='Test editor';Time=$now;Pinned=$true}),
        @('Tasks','TaskItem',@{Id='sweep-task';Title='Review the screenshots';Notes='Check spacing and keyboard actions.';Due=[datetime]::Today.Ticks;Priority=2;Modified=$now}),
        @('Activity','ActivityRecord',@{Id='sweep-activity';Path=$md;Process='Text Editor';Event='Opened';Time=$now;Size=100}),
        @('Tagged','TaggedFile',@{Id='sweep-file';Path=$md;Category='Review';Added=$now}),
        @('Cards','CardDef',@{Id='sweep-card';Title='Work notes';Visible=$false})
    )
    foreach ($record in $records) { [void](Add-Record ($storeT.GetField($record[0]).GetValue($null)) $record[1] $record[2]) }
}
# The Accounts page uses demo definitions, so it never runs a real sign-in status command.
$defs = $asm.GetType('StickyPro.AgentStore').GetProperty('Defs').GetValue($null, $null)
foreach ($provider in $defs) { $provider.Id = 'demo-' + $provider.Id; $provider.Exe = Join-Path $fixture 'uninstalled-demo-cli.exe' }
$ui = $asm.GetType('StickyPro.Ui')

$problems = New-Object System.Collections.Generic.List[string]
$errors = New-Object System.Collections.Generic.List[string]
$timings = New-Object System.Collections.Generic.List[string]
function Note($kind, $where, $what) { $problems.Add("| $kind | $where | $what |") }
function Pump($ms) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) {
        try { [Windows.Forms.Application]::DoEvents() } catch { $errors.Add("DoEvents: " + $_.Exception.InnerException.Message + $_.Exception.Message) }
        Start-Sleep -Milliseconds 10
    }
}
function Shot($form, $file) {
    $bmp = New-Object Drawing.Bitmap $form.Width, $form.Height
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    try { $printed = [SweepNative]::PrintWindow($form.Handle, $hdc, 2) }
    finally { $g.ReleaseHdc($hdc); $g.Dispose() }
    if (-not $printed) { $bmp.Dispose(); throw "PrintWindow failed for $file" }
    $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png)
    return $bmp
}
# Layout checks on the visible control tree of a page.
function Check-Layout($ctl, $where, [int]$depth = 0) {
    if ($depth -gt 14 -or -not $ctl.Visible) { return }
    foreach ($c in $ctl.Controls) {
        if (-not $c.Visible -or $c.Width -le 0 -or $c.Height -le 0) { continue }
        $p = $c.Parent
        $scrolls = $p -is [Windows.Forms.ScrollableControl] -and $p.AutoScroll
        if (-not $scrolls -and $c.Dock -eq 'None') {
            $over = [Math]::Max($c.Right - $p.ClientSize.Width, $c.Bottom - $p.ClientSize.Height)
            if ($over -gt 3 -and $p.ClientSize.Width -gt 0) { Note 'cut off' $where ("{0} '{1}' sticks out {2}px of {3}" -f $c.GetType().Name, ($c.Text -replace '\|', '/').Substring(0, [Math]::Min(30, $c.Text.Length)), $over, $p.GetType().Name) }
        }
        if ($c -is [Windows.Forms.ButtonBase] -and $c.Text.Length -gt 0 -and -not $c.AutoSize) {
            $need = [Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font).Width + 8
            if ($need -gt $c.Width + 2) { Note 'text cut' $where ("button '{0}' needs {1}px, has {2}px" -f $c.Text, $need, $c.Width) }
        }
        if ($script:dark -and $c.Width * $c.Height -gt 4000 -and -not ($c -is [Windows.Forms.PictureBox]) -and $c.GetType().Name -notmatch 'WebView|Swatch|Note') {
            $b = $c.BackColor; if (($b.R * 299 + $b.G * 587 + $b.B * 114) / 1000 -gt 200 -and $b.A -gt 200) { Note 'light in dark' $where ("{0} '{1}' has a light background {2}" -f $c.GetType().Name, ($c.Text -replace '\|', '/').Substring(0, [Math]::Min(30, $c.Text.Length)), $b.Name) }
        }
        if ($c -is [Windows.Forms.Label] -and $c.Dock -ne 'Fill' -and $c.Height -lt 2 * $c.Font.Height -and -not $c.AutoSize -and -not $c.AutoEllipsis -and $c.Text.Length -gt 0 -and $c.Text.IndexOf("`n") -lt 0) {
            $need = [Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font).Width
            if ($need -gt $c.Width + 4 -and $c.Width -lt 400) { Note 'text cut' $where ("label '{0}' needs {1}px, has {2}px" -f $c.Text.Substring(0, [Math]::Min(40, $c.Text.Length)), $need, $c.Width) }
        }
        if ($c.ContextMenuStrip -ne $null) {
            try { $c.ContextMenuStrip.Show($c, (New-Object Drawing.Point 2, 2)); Pump 30; $c.ContextMenuStrip.Close() }
            catch { $errors.Add("$where menu on $($c.GetType().Name): " + $_.Exception.InnerException.Message) }
        }
        Check-Layout $c $where ($depth + 1)
    }
}
# Mostly one colour in the content area = probably an empty or broken page.
function Check-Blank($bmp, $where) {
    $colors = @{}; $n = 0
    for ($y = 140; $y -lt $bmp.Height - 40; $y += 37) { for ($x = 300; $x -lt $bmp.Width - 80; $x += 41) { $colors[$bmp.GetPixel($x, $y).ToArgb()] = 1; $n++ } }
    if ($n -gt 50 -and $colors.Count -le 3) { Note 'looks empty' $where ("only {0} colours in the content area" -f $colors.Count) }
}

function Offscreen($window) {
    $window.ShowInTaskbar = $false
    $window.StartPosition = 'Manual'
    $window.Location = [Drawing.Point]::new([Windows.Forms.SystemInformation]::VirtualScreen.Left - 3000, 0)
    $handle = $window.Handle
    [void][SweepNative]::SetWindowLong($handle, -20, ([SweepNative]::GetWindowLong($handle, -20) -bor 0x08000000))
    $window.Show()
}
function Prepare-Chrome($form) {
    # Stop the cold load before the page can read Chrome profiles or bookmarks.
    $form.GetType().GetMethod('EnsurePage', $private).Invoke($form, @('chrome'))
    $chrome = $form.GetType().GetField('chrome', $private).GetValue($form)
    foreach ($view in $chrome.GetType().GetField('views', $private).GetValue($chrome)) {
        $view.GetType().GetField('loaded', $private).SetValue($view, $true)
    }
}
function Select-View($form, [string]$route) {
    $parent, $sub = $route.Split('/')
    $form.ShowPage($parent)
    if ($sub) {
        $control = $form.GetType().GetField('loadedPages', $private).GetValue($form)[$parent.Split(':')[0]]
        if ($parent -eq 'system:drives') { $control = $control.GetType().GetField('views', $private).GetValue($control)[2] }
        $control.GetType().GetField('tabs', $private).GetValue($control).Select([int]$sub)
    }
}
function Use-SampleContent($form, [string]$route) {
    $pages = $form.GetType().GetField('loadedPages', $private).GetValue($form)
    if ($route -eq 'snip') {
        $panel = $pages['snip'].Controls | Where-Object { $_.GetType().Name -eq 'SnipPanel' }
        $list = $panel.GetType().GetField('list', $private).GetValue($panel)
        $list.Items.Clear()
    }
    if ($route -eq 'power/2') {
        $hosts = $pages['power'].GetType().GetField('views', $private).GetValue($pages['power'])[2]
        $editor = $hosts.Controls | Where-Object { $_ -is [Windows.Forms.TextBox] }
        $editor.Text = "# Sample hosts file`r`n127.0.0.1 localhost`r`n::1       localhost"
    }
}
$routes = 'dashboard,tasks,notes,prompts,clips,agents,terminals,explorer,markdown,browser,documents,accounts,snip,chrome,chrome:extensions,power/0,power/1,power/2,power/3,system:procs,system:startup,system:drives/0,system:drives/1,system:drives/2,system:uninstall,cards,activity,tagged,shortcuts/0,shortcuts/1,shortcuts/2,shortcuts/3,settings'.Split(',')
$pages = $routes | Where-Object { $_ -notin 'settings','power/2' }
$sheets = @()
foreach ($look in $Looks) {
    $design, $mode = $look.Split('-')
    $dark = $mode -eq 'dark'; $script:dark = $dark
    $st.Design = $design; $st.Dark = $dark; $st.Theme = $(if ($design -eq 'soft') { 8 } elseif ($design -eq 'serq') { 7 } elseif ($design -eq 'nexus') { 6 } else { 0 }); $st.AccentColor = ''
    $ui.GetField('Dark').SetValue($null, $dark); $ui.GetField('Nexus').SetValue($null, $design -ne 'classic')
    $ui.GetField('Navy').SetValue($null, $design -eq 'serq' -or $design -eq 'soft'); $ui.GetField('Soft').SetValue($null, $design -eq 'soft'); $ui.GetField('ThemeIndex').SetValue($null, $st.Theme)
    $ui.GetMethod('ApplyLook').Invoke($null, $null)
    foreach ($size in $Sizes) {
        $w, $h = $size.Split('x') | ForEach-Object { [int]$_ }
        $form = [Activator]::CreateInstance($asm.GetType('StickyPro.MainForm'), $true)
        $form.Size = [Drawing.Size]::new($w, $h)
        Prepare-Chrome $form
        Offscreen $form
        Pump 150
        $show = $form.GetType().GetMethod('ShowPage', [Type[]]@([string]))
        $thumbs = @()
        foreach ($p in $routes) {
            $where = "$look $size $p"
            $sw = [Diagnostics.Stopwatch]::StartNew()
            $target = $form
            try {
                if ($p -eq 'settings') {
                    $target = [Activator]::CreateInstance($asm.GetType('StickyPro.SettingsForm'), [Reflection.BindingFlags]'Instance,Public,NonPublic', $null, [object[]]@($false), $null)
                    Offscreen $target
                } else { Select-View $form $p }
                $openMs = $sw.ElapsedMilliseconds
                Pump $(if ($p -eq 'markdown' -or $p -eq 'explorer') { 450 } else { 150 })
                if (-not $Data) { Use-SampleContent $form $p }
                if ($openMs -gt 400) { $timings.Add("$where opened in $openMs ms") }
                $name = $p -replace '[:/]', '-'
                $file = Join-Path $Out "pages\$look-$size-$name.png"
                $bmp = Shot $target $file
                try { Check-Blank $bmp $where } finally { $bmp.Dispose() }
                $thumbs += @{ File = $file; Name = $name }
                Check-Layout $target $where
            } catch { $errors.Add("$where : " + $_.Exception.GetBaseException().ToString()) }
            finally { if ($target -ne $form) { $target.Dispose() } }
        }
        # Use actual aspect ratios and label each route so omissions are visible.
        $tw = 400; $th = [int](400 * $h / $w) + 26
        $rows = [int][Math]::Ceiling($routes.Count / 5.0)
        $sheet = New-Object Drawing.Bitmap ($tw * 5), ($th * $rows)
        $g = [Drawing.Graphics]::FromImage($sheet); $g.Clear([Drawing.Color]::FromArgb(27,33,44)); $g.InterpolationMode = 'HighQualityBicubic'
        for ($i = 0; $i -lt $thumbs.Count; $i++) {
            $x = ($i % 5) * $tw; $y = [int][Math]::Floor($i / 5) * $th
            $im = [Drawing.Image]::FromFile($thumbs[$i].File)
            try {
                $ratio = [Math]::Min(($tw - 6) / $im.Width, ($th - 28) / $im.Height)
                $g.DrawImage($im, $x + 3, $y + 23, [int]($im.Width * $ratio), [int]($im.Height * $ratio))
                $g.DrawString($thumbs[$i].Name, [Drawing.SystemFonts]::MessageBoxFont, [Drawing.Brushes]::White, $x + 4, $y + 2)
            } finally { $im.Dispose() }
        }
        $g.Dispose(); $sf = Join-Path $Out "sheet-$look-$size.png"; $sheet.Save($sf, [Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose(); $sheets += $sf
        $form.Dispose()
    }
}

# ---------- stress ----------
if (-not $SkipStress) {
$form = [Activator]::CreateInstance($asm.GetType('StickyPro.MainForm'), $true)
$form.Size = [Drawing.Size]::new(1400, 860)
Prepare-Chrome $form
Offscreen $form
Pump 200
$show = $form.GetType().GetMethod('ShowPage', [Type[]]@([string]))
$rnd = New-Object Random 7
$sw = [Diagnostics.Stopwatch]::StartNew(); $worst = 0; $worstPage = ''
for ($i = 0; $i -lt 300; $i++) {
    $p = $pages[$rnd.Next($pages.Count)]; $t0 = $sw.ElapsedMilliseconds
    try { Select-View $form $p; [Windows.Forms.Application]::DoEvents() } catch { $errors.Add("stress switch $p : " + $_.Exception.InnerException.Message) }
    $d = $sw.ElapsedMilliseconds - $t0; if ($d -gt $worst) { $worst = $d; $worstPage = $p }
}
$timings.Add("300 random page switches: $($sw.ElapsedMilliseconds) ms total, worst $worst ms ($worstPage)")
foreach ($p in 'agents', 'terminals', 'explorer', 'tasks', 'prompts', 'markdown', 'browser') {
    $show.Invoke($form, @($p)) | Out-Null; Pump 200
    $sw.Restart(); $worst = 0
    for ($k = 0; $k -lt 40; $k++) {
        $t0 = $sw.ElapsedMilliseconds
        try { $form.Size = New-Object Drawing.Size((900 + ($k * 37) % 1000), (600 + ($k * 23) % 450)); [Windows.Forms.Application]::DoEvents() } catch { $errors.Add("stress resize $p : " + $_.Exception.InnerException.Message) }
        $d = $sw.ElapsedMilliseconds - $t0; if ($d -gt $worst) { $worst = $d }
    }
    $timings.Add("resize x40 on $p : worst step $worst ms")
}
$search = $asm.GetType('StickyPro.GlobalSearch')
$find = $search.GetMethod('Find', [Reflection.BindingFlags]'NonPublic,Static')
$sw.Restart()
foreach ($q in 'a', 're', 'rev', 'review', 'task', 'note', 'C:\', 'http', '#tag', '  x  ', ('z' * 200), (-join [char[]](0x062A,0x0642,0x0631,0x064A,0x0631)), '"quote"', '[[link]]') {
    try { $n = @($find.Invoke($null, @($form, $q)) | Where-Object { $_ -ne $null }).Count } catch { $errors.Add("search '$q': " + $_.Exception.InnerException.Message) }
}
$timings.Add("14 searches (all results): $($sw.ElapsedMilliseconds) ms")
$form.Dispose()
}

# errors the app wrote itself
$log = Join-Path $fixture 'error.log'
if (Test-Path $log) { Get-Content $log | Where-Object { $_ -match '^\d{4}-' } | ForEach-Object { $errors.Add('app log: ' + $_) } }

$r = New-Object Text.StringBuilder
[void]$r.AppendLine("# UI sweep $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
[void]$r.AppendLine("Looks: $($Looks -join ', ')  Sizes: $($Sizes -join ', ')  Data: $(if ($Data) { 'copy of ' + $Data } else { 'sample fixture' })")
[void]$r.AppendLine("Routes: $($routes.Count) | Screenshots: $(@(Get-ChildItem (Join-Path $Out 'pages') -Filter *.png).Count)")
[void]$r.AppendLine("Browser shell and native Markdown preview captured in offline mode.")
[void]$r.AppendLine("`n## Errors ($($errors.Count))"); foreach ($e in ($errors | Select-Object -Unique)) { [void]$r.AppendLine("- $e") }
[void]$r.AppendLine("`n## Layout problems ($($problems.Count))`n| Kind | Where | What |`n|---|---|---|"); foreach ($p in ($problems | Select-Object -Unique)) { [void]$r.AppendLine($p) }
[void]$r.AppendLine("`n## Timings"); foreach ($t in $timings) { [void]$r.AppendLine("- $t") }
[void]$r.AppendLine("`n## Contact sheets"); foreach ($s in $sheets) { [void]$r.AppendLine("- $s") }
[IO.File]::WriteAllText((Join-Path $Out 'report.md'), $r.ToString())
Write-Host ("SWEEP DONE: {0} errors, {1} layout problems. Report: {2}" -f $errors.Count, $problems.Count, (Join-Path $Out 'report.md'))
