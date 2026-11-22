# Browser automation check against the RUNNING All in One app (named pipe, same path an agent's MCP calls take).
# Serves tools\browser-test-site on 127.0.0.1 (Python http.server), works only in its own new tabs and closes them.
# The app must run the current build (build.ps1, then start AllInOne.exe).
$ErrorActionPreference = 'Stop'
$site = Join-Path $PSScriptRoot 'browser-test-site'
$port = 8765 + (Get-Random -Maximum 200)
$script:passed = 0
function Ok($v, [string]$m) { if (-not $v) { throw "FAIL: $m" }; $script:passed++; Write-Host "  ok  $m" }
$pipeName = 'AllInOne.Api.v1.' + [Diagnostics.Process]::GetCurrentProcess().SessionId
function Tool([string]$name, [hashtable]$a = @{}) {
    $p = New-Object IO.Pipes.NamedPipeClientStream('.', $pipeName, [IO.Pipes.PipeDirection]::InOut)
    $p.Connect(5000)
    try {
        $enc = New-Object Text.UTF8Encoding($false)
        $w = New-Object IO.StreamWriter($p, $enc); $w.AutoFlush = $true; $w.NewLine = "`n"
        $r = New-Object IO.StreamReader($p, $enc)
        $w.WriteLine((@{ tool = $name; args = $a } | ConvertTo-Json -Depth 8 -Compress))
        $ans = $r.ReadLine() | ConvertFrom-Json
    } finally { $p.Dispose() }
    if (-not $ans.ok) { throw "$name failed: $($ans.error)" }
    if ($ans.text -match '^\s*[\[{"]') { return $ans.text | ConvertFrom-Json }
    return $ans.text
}
function Err([string]$name, [hashtable]$a) { try { $null = Tool $name $a; return $null } catch { return $_.Exception.Message } }
function Ref($snap, [string]$role, [string]$like) { $e = @($snap.elements | Where-Object { $_.role -eq $role -and $_.name -like $like })[0]; if (-not $e) { throw "no $role '$like' in snapshot" }; return $e.ref }
function Text([string]$id) { Tool 'browser_run_js' @{ script = "document.getElementById('$id').textContent" } }

$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) { throw 'Python is needed for the local test web server.' }
$server = Start-Process $py.Source "-m http.server $port --bind 127.0.0.1 --directory `"$site`"" -WindowStyle Hidden -PassThru
$base = "http://127.0.0.1:$port"
$myTabs = @(); $dlFile = $null
try {
    Start-Sleep 1
    $t = Tool 'browser_tabs' @{ action = 'new'; url = "$base/a.html" }
    $tab = $t.active; $myTabs += $tab
    Ok ($t.tabs[$tab].url -like "*a.html") 'new tab opened on the test page'
    $null = Tool 'browser_wait' @{ text = 'Bottom of page'; tab = $tab }

    $s = Tool 'browser_snapshot' @{ tab = $tab }
    Ok ($s.title -eq 'Automation test A' -and $s.count -ge 10) "snapshot lists $($s.count) elements with refs"
    $dis = @($s.elements | Where-Object { $_.name -eq 'Disabled one' })[0]
    Ok ($dis.disabled -eq $true) 'snapshot reports disabled state'
    Ok (@($s.elements | Where-Object { $_.frame -eq $true -and $_.name -eq 'Frame button' }).Count -eq 1) 'snapshot includes iframe elements'

    $add = Ref $s 'button' 'Add one'
    $r = Tool 'browser_click' @{ ref = $add; tab = $tab }; $r = Tool 'browser_click' @{ ref = $add; tab = $tab }
    Ok ((Text 'count') -eq 'Count: 2') 'click by ref (twice)'
    Ok ((Text 'trust') -eq 'trusted: true') 'clicks are trusted input like a real mouse'
    $null = Tool 'browser_click' @{ text = 'Add one'; tab = $tab }
    Ok ((Text 'count') -eq 'Count: 3') 'click by visible text'
    $e = Err 'browser_click' @{ text = 'Disabled one'; tab = $tab }
    Ok ($e -like '*disabled*') 'clicking a disabled button is refused with a reason'

    $r = Tool 'browser_fill' @{ label = 'Name'; value = 'Ahmed'; tab = $tab }
    Ok ((Tool 'browser_run_js' @{ script = "document.getElementById('name').value" }) -eq 'Ahmed') 'fill by label'
    $null = Tool 'browser_key' @{ selector = '#name'; keys = 'Control+A'; tab = $tab }
    $null = Tool 'browser_key' @{ type_text = 'Sara'; tab = $tab }
    Ok ((Tool 'browser_run_js' @{ script = "document.getElementById('name').value" }) -eq 'Sara') 'Control+A then typing replaces the text'
    $null = Tool 'browser_key' @{ keys = 'ArrowLeft Backspace'; tab = $tab }
    $v = Tool 'browser_run_js' @{ script = "document.getElementById('name').value" }; Ok ($v -eq 'Saa') "arrow and backspace keys edit like a keyboard (Sara -> $v)"
    $k = Text 'keys'; Ok ($k -like '*a ArrowLeft Backspace*' -and $k -notlike '*untrusted*') "key events reach the page as trusted ($k)"

    $r = Tool 'browser_select' @{ label = 'Color'; value = 'Green'; tab = $tab }
    Ok ((Tool 'browser_run_js' @{ script = "document.getElementById('color').value" }) -eq 'g') 'select option by text'
    $s = Tool 'browser_snapshot' @{ tab = $tab }
    $r = Tool 'browser_fill' @{ fields = @{ (Ref $s 'textbox' 'Name') = 'Mona'; '#color' = 'Blue' }; tab = $tab }
    Ok ((Tool 'browser_run_js' @{ script = "document.getElementById('name').value+'/'+document.getElementById('color').value" }) -eq 'Mona/b') 'fill several fields at once (text + dropdown)'
    $null = Tool 'browser_click' @{ text = 'Agree to terms'; tab = $tab }
    Ok ((Tool 'browser_run_js' @{ script = "document.getElementById('agree').checked" }) -eq $true) 'checkbox clicked through its label'

    $null = Tool 'browser_hover' @{ text = 'Hover me'; tab = $tab }
    Ok ((Text 'hovstate') -eq 'hover: yes') 'hover moves the mouse over the element'

    $null = Tool 'browser_click' @{ text = 'Frame button'; tab = $tab }
    Ok ((Tool 'browser_run_js' @{ script = "document.getElementById('fr').contentDocument.querySelector('button').textContent" }) -eq 'frame clicked') 'click inside an iframe'

    $sc = Tool 'browser_scroll' @{ to = 'bottom'; tab = $tab }
    Ok ($sc.at_bottom -eq $true) 'scroll to bottom'
    $sc = Tool 'browser_scroll' @{ dy = -600; tab = $tab }
    Ok ($sc.at_bottom -eq $false) 'mouse-wheel scroll up'

    $d = Tool 'browser_dialogs' @{ mode = 'accept' }
    $null = Tool 'browser_click' @{ text = 'Confirm me'; tab = $tab }
    Ok ((Text 'conf') -eq 'confirmed: true') 'confirm dialog accepted automatically (mode=accept)'
    $d = Tool 'browser_dialogs' @{ mode = 'dismiss' }
    $null = Tool 'browser_click' @{ text = 'Confirm me'; tab = $tab }
    Ok ((Text 'conf') -eq 'confirmed: false') 'confirm dialog dismissed (mode=dismiss)'
    $d = Tool 'browser_dialogs' @{ mode = 'user' }
    Ok ($d.recent[0].message -eq 'Sure?' -and $d.mode -eq 'user') 'dialog log recorded, mode back to the user'

    $c = Tool 'browser_console' @{ tab = $tab }
    Ok (@($c.messages | Where-Object { $_.text -like 'hello-console 42*' }).Count -ge 1) 'console messages captured'
    Ok (@($c.messages | Where-Object { $_.level -eq 'error' -and $_.text -like '*test-page-error*' }).Count -ge 1) 'page errors captured'
    $n = Tool 'browser_network' @{ failed_only = $true; tab = $tab }
    Ok (@($n.requests | Where-Object { $_.url -like '*missing-file.json' -and $_.status -eq 404 }).Count -ge 1) 'failed network request (404) listed'

    $null = Tool 'browser_cookies' @{ action = 'set'; name = 'aio_test'; value = 'v1'; tab = $tab }
    $ck = Tool 'browser_cookies' @{ tab = $tab }
    Ok (@($ck.cookies | Where-Object { $_.name -eq 'aio_test' -and $_.value -eq 'v1' }).Count -eq 1) 'cookie set and listed'
    $null = Tool 'browser_cookies' @{ action = 'delete'; name = 'aio_test'; tab = $tab }
    $ck = Tool 'browser_cookies' @{ tab = $tab }
    Ok (@($ck.cookies | Where-Object { $_.name -eq 'aio_test' }).Count -eq 0) 'cookie deleted'

    $up1 = Join-Path $env:TEMP 'aio-upload-1.txt'; $up2 = Join-Path $env:TEMP 'aio-upload-2.txt'
    'one' | Set-Content $up1; 'two' | Set-Content $up2
    $u = Tool 'browser_upload' @{ selector = '#file'; paths = @($up1, $up2); tab = $tab }
    Ok ((Text 'files') -eq 'files: aio-upload-1.txt,aio-upload-2.txt') 'two files attached to the upload field'
    Remove-Item $up1, $up2 -ErrorAction SilentlyContinue

    $shot = Tool 'browser_screenshot' @{ tab = $tab }
    Ok ((Test-Path $shot.path) -and $shot.width -gt 100) "screenshot saved ($($shot.width)x$($shot.height))"
    $full = Tool 'browser_screenshot' @{ full_page = $true; tab = $tab }
    Ok ($full.height -gt $shot.height) "full-page screenshot is taller ($($full.height) px)"
    $el = Tool 'browser_screenshot' @{ selector = '#t'; tab = $tab }
    Ok ($el.width -lt $shot.width -and $el.height -lt 200) "element screenshot ($($el.width)x$($el.height))"
    $pdf = Tool 'browser_pdf' @{ tab = $tab }
    Ok ((Test-Path $pdf.path) -and $pdf.bytes -gt 1000) 'page saved as PDF'
    Remove-Item $shot.path, $full.path, $el.path, $pdf.path -ErrorAction SilentlyContinue

    $x = Tool 'browser_extract' @{ what = 'tables'; tab = $tab }
    Ok ($x.tables[0].rows[1][0] -eq 'Bolt' -and $x.tables[0].rows[2][1] -eq '30') 'table extracted as rows'
    $x = Tool 'browser_extract' @{ what = 'forms'; tab = $tab }
    Ok (@($x.forms[0].fields | Where-Object { $_.field -eq 'name' }).Count -eq 1) 'form fields extracted'
    $x = Tool 'browser_extract' @{ what = 'meta'; tab = $tab }
    Ok ($x.description -like '*automation test page*') 'meta tags extracted'

    $m = Tool 'browser_emulate' @{ device = 'phone'; tab = $tab }
    $w = Tool 'browser_run_js' @{ script = 'screen.width + "|" + navigator.userAgent' }
    Ok ($w -like '390|*iPhone*') "phone view (width and user agent: $($w.Substring(0,[Math]::Min(60,$w.Length))))"
    $null = Tool 'browser_emulate' @{ device = 'desktop'; tab = $tab }
    $w = Tool 'browser_run_js' @{ script = 'screen.width + "|" + navigator.userAgent' }
    Ok ($w -notlike '390|*' -and $w -notlike '*iPhone*') "desktop view restored ($($w.Substring(0,[Math]::Min(60,$w.Length))))"

    $null = Tool 'browser_click' @{ text = 'Download file'; tab = $tab }
    $dl = Tool 'browser_downloads' @{ action = 'wait'; timeout_ms = 20000 }
    $dlFile = $dl.file
    Ok ($dl.state -eq 'completed' -and (Test-Path $dl.file) -and (Get-Content $dl.file -Raw) -eq 'hello download') "download finished ($($dl | ConvertTo-Json -Compress))"

    $r = Tool 'browser_click' @{ text = 'Next page'; tab = $tab }
    Ok ($r.navigated -eq $true -and $r.title -eq 'Automation test B') 'click on a link waits for the new page'
    $r = Tool 'browser_navigate' @{ action = 'back'; tab = $tab }
    Ok ($r.title -eq 'Automation test A' -and $r.ok -eq $true) 'navigate back'
    $r = Tool 'browser_navigate' @{ url = "$base/nothing-here.html"; tab = $tab }
    Ok ($r.http_status -eq 404) 'navigate reports HTTP status 404'
    $r = Tool 'browser_navigate' @{ url = "$base/a.html"; tab = $tab }
    $e = Err 'browser_click' @{ ref = 'e999'; tab = $tab }
    Ok ($e -like '*snapshot*') 'unknown ref explains to take a new snapshot'
    $r = Tool 'browser_click' @{ text = 'Open B in new tab'; tab = $tab }
    Ok ($r.opened_tab -gt $tab -and $r.title -eq 'Automation test B') 'link to a new tab is followed and reported'
    $myTabs += $r.opened_tab
    $w = Tool 'browser_wait' @{ selector = '#nope'; timeout_ms = 800; tab = $tab } 2>$null
}
catch { if ($_.Exception.Message -notlike 'browser_wait failed: Timed out*') { throw } else { Ok $true 'wait times out with a clear message' } }
finally {
    foreach ($i in ($myTabs | Sort-Object -Descending)) { try { $null = Tool 'browser_tabs' @{ action = 'close'; index = $i } } catch {} }
    try { $null = Tool 'browser_dialogs' @{ mode = 'user' } } catch {}
    if ($dlFile) { Remove-Item $dlFile -ErrorAction SilentlyContinue }
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
}
Write-Host "BROWSER AUTOMATION PASSED ($script:passed checks)"
