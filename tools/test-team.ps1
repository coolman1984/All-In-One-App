# Team chat + update distribution tests. Uses ONLY temporary folders as the "shared folder" (never S:\ or any
# production share) and a temporary signing key that is deleted at the end.
# Needs build-test\StickyNotesPro.test.exe (tools\test-build.ps1).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$root = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $root 'build-test\StickyNotesPro.test.exe'
if (-not (Test-Path $exePath)) { & (Join-Path $PSScriptRoot 'test-build.ps1') | Out-Null }
$assembly = [Reflection.Assembly]::LoadFile($exePath)
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$script:passed = 0
function Assert($value, [string]$message) { if (-not $value) { throw "FAIL: $message" }; $script:passed++; Write-Host "  ok  $message" }
function T([string]$name) { $assembly.GetType('StickyPro.' + $name) }
function Call([string]$type, [string]$method, [object[]]$arguments) {
    for ($i = 0; $i -lt $arguments.Count; $i++) { if ($null -ne $arguments[$i]) { $arguments[$i] = $arguments[$i].PSObject.BaseObject } }
    try { (T $type).GetMethod($method, $static).Invoke($null, $arguments) }
    catch [Reflection.TargetInvocationException] { throw $_.Exception.InnerException }
}
function Throws([scriptblock]$block) {
    try { & $block; return $null }
    catch { $x = $_.Exception; while (($x -is [Management.Automation.MethodInvocationException] -or $x -is [Reflection.TargetInvocationException]) -and $x.InnerException) { $x = $x.InnerException }; return $x }
}
function Delegate($type, [scriptblock]$sb) { [Management.Automation.LanguagePrimitives]::ConvertTo($sb, $type) }
$MsgAction = [Action``1].MakeGenericType((T 'TeamMsg'))
$ChAction = [Action``1].MakeGenericType((T 'ReleaseChannels'))

$fixture = Join-Path $root ('build-test\team-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $fixture | Out-Null
$store = T 'Store'
$store.GetField('Dir', $static).SetValue($null, $fixture)
$share = Join-Path $fixture 'share'
function Client([string]$name) {
    $c = [Activator]::CreateInstance((T 'TeamClient'), @([string](Join-Path $fixture $name)))
    $c.Local.Name = $name; $c.Local.KeyContainer = 'AIO-Test-' + [guid]::NewGuid().ToString('N'); $c.Local.DeviceKeyContainer = 'AIO-TestDev-' + [guid]::NewGuid().ToString('N'); $c.SaveLocal()
    return $c
}
function Rand([string]$path, [int]$size) { $b = New-Object byte[] $size; (New-Object Random).NextBytes($b); [IO.File]::WriteAllBytes($path, $b) }
$A = Client 'Admin'; $B = Client 'Bea'; $C = Client 'Cem'

try {
    Write-Host 'Team setup'
    $A.Create($share, 'Test team', 'correct horse battery')
    $null = Call 'TeamSecurity' 'Unlock' @($A, 'correct horse battery')
    Assert ($A.IsAdmin) 'creator is admin and holds the release key'
    $B.Join($share); $C.Join($share)
    Call 'TeamSecurity' 'Approve' @($A, $B.Me); Call 'TeamSecurity' 'Approve' @($A, $C.Me)
    foreach ($x in $A, $B, $C) { $x.RefreshPolicy() }
    foreach ($x in $A, $B, $C) { $x.Heartbeat($true) }
    Assert (-not $B.IsAdmin) 'joined member is not admin'
    Assert ($B.KeyTrusted) 'member pinned the release key'
    $ids = @($B.Rooms($true) | ForEach-Object Id)
    Assert ($ids -contains 'general' -and $ids -contains 'updates') 'general and updates rooms exist'

    Write-Host 'Chat delivery, offline, duplicates, order'
    $m1 = $A.Post('general', 'text', 'hello from admin', $null)
    $fresh = $B.Poll('general')
    Assert (@($fresh | Where-Object Id -eq $m1.Id).Count -eq 1) 'member receives the message'
    Rename-Item $share ($share + '-off')
    $m2 = $B.Post('general', 'text', 'written while offline', $null)
    Assert ($B.Local.Outbox.Count -eq 1) 'offline message waits in the outbox'
    $B2 = [Activator]::CreateInstance((T 'TeamClient'), @([string](Join-Path $fixture 'Bea')))
    Assert ($B2.Local.Outbox.Count -eq 1) 'outbox survives an app restart'
    Rename-Item ($share + '-off') $share
    Assert ($B.Flush() -eq 1 -and $B.Local.Outbox.Count -eq 0) 'outbox is sent when the folder is back'
    $bfile = Join-Path $share ('rooms\general\msgs\' + $B.Me.ToLower() + '.jsonl')
    $line = [IO.File]::ReadAllLines($bfile)[-1]
    [IO.File]::AppendAllText($bfile, $line + "`n")   # a retried write
    $null = $A.Poll('general'); $pending = $null
    $all = $A.Messages('general', [ref]$pending)
    Assert (@($all | Where-Object Id -eq $m2.Id).Count -eq 1) 'a duplicated write shows once'
    $ts = @($all | ForEach-Object Ts)
    Assert ((($ts | Sort-Object) -join ',') -eq ($ts -join ',')) 'messages are in time order'
    for ($i = 0; $i -lt 30; $i++) { $null = $A.Post('general', 'text', "burst $i", $null); $null = $B.Post('general', 'text', "burst b$i", $null) }
    $null = $C.Poll('general'); $all = $C.Messages('general', [ref]$pending)
    Assert (@($all | Where-Object { $_.Text -like 'burst*' }).Count -eq 60) 'two simultaneous senders: all 60 messages arrive'

    Write-Host 'Reactions, replies, edits, deletes, pins, reads, search'
    $id = $m1.Id
    $null = $B.Post('general', 'reaction', 'like', (Delegate $MsgAction { param($x) $x.Target = $id }.GetNewClosure()))
    $null = $C.Post('general', 'reaction', 'like', (Delegate $MsgAction { param($x) $x.Target = $id }.GetNewClosure()))
    $null = $B.Post('general', 'pin', '', (Delegate $MsgAction { param($x) $x.Target = $id }.GetNewClosure()))
    $null = $B.Post('general', 'edit', 'hacked', (Delegate $MsgAction { param($x) $x.Target = $id }.GetNewClosure()))
    $null = $A.Post('general', 'edit', 'hello (edited)', (Delegate $MsgAction { param($x) $x.Target = $id }.GetNewClosure()))
    $r = $B.Post('general', 'text', 'a reply', (Delegate $MsgAction { param($x) $x.ReplyTo = $id }.GetNewClosure()))
    $null = $A.Poll('general'); $view = $A.View('general')
    $v1 = $view | Where-Object { $_.M.Id -eq $id }
    Assert ($v1.Reactions['like'].Count -eq 2) 'two reactions counted'
    Assert ($v1.Pinned) 'message pinned'
    Assert ($v1.Text -eq 'hello (edited)' -and $v1.Edited) 'only the author can edit'
    Assert (($view | Where-Object { $_.M.Id -eq $r.Id }).Reply.Id -eq $id) 'reply links to its message'
    $A.MarkRead('general')
    Assert ($B.Reads('general').Count -ge 1) 'read receipt visible to others'
    Assert ($B.Search('offline').Count -ge 1) 'search finds messages across rooms'
    $dm = $A.DirectWith(($A.Devices($true) | Where-Object Name -eq 'Bea'))
    Assert (@($C.Rooms($true) | Where-Object Id -eq $dm.Id).Count -eq 0 -and @($B.Rooms($true) | Where-Object Id -eq $dm.Id).Count -eq 1) 'private conversation visible only to its members'

    Write-Host 'Big files: interrupted and resumed, checksum'
    $big = Join-Path $fixture 'big.bin'; Rand $big (9MB + 12345)
    $count = 0
    $stop = [Func[bool]] { $script:count++; return ($script:count -gt 1) }
    $e = Throws { $null = $B.SendFile('general', $big, $null, $stop, $null) }
    Assert ($e -is [OperationCanceledException]) 'upload interrupted'
    $up = $B.Local.Uploads[0]
    $part = Get-ChildItem (Join-Path $share "files\$($up.Id)") -Filter '*.part'
    Assert ($part.Length -gt 0 -and $part.Length -lt (Get-Item $big).Length) 'partial upload kept'
    $fm = $B.SendFile('general', $big, $null, $null, $up.Id)
    Assert ($B.Local.Uploads.Count -eq 0 -and $fm.Sha -eq (Get-FileHash $big -Algorithm SHA256).Hash.ToLower()) 'upload resumed and verified'
    $dl = Join-Path $fixture 'dl'; New-Item -ItemType Directory $dl | Out-Null
    $got = $A.Download($fm, $dl, $null, $null)
    Assert ((Get-FileHash $got -Algorithm SHA256).Hash -eq (Get-FileHash $big -Algorithm SHA256).Hash) 'download identical'
    $shared = Join-Path $share "files\$($fm.FileId)\data.bin"
    $bytes = [IO.File]::ReadAllBytes($shared); $bytes[100] = $bytes[100] -bxor 255; [IO.File]::WriteAllBytes($shared, $bytes)
    $dl2 = Join-Path $fixture 'dl2'; New-Item -ItemType Directory $dl2 | Out-Null
    $e = Throws { $null = $A.Download($fm, $dl2, $null, $null) }
    Assert ($e -ne $null -and -not (Test-Path (Join-Path $dl2 $fm.FileName))) 'damaged shared file refused'

    Write-Host 'Releases: publish, channels, signatures'
    $v1exe = Join-Path $fixture 'v1.exe'; Rand $v1exe 3MB
    $v2exe = Join-Path $fixture 'v2.exe'; Rand $v2exe 3MB
    $oldexe = Join-Path $fixture 'old.exe'; Rand $oldexe 2MB
    $e = Throws { $null = Call 'Releases' 'Publish' @($B, $v1exe, 'x', '', 'pilot', $false, $null) }
    Assert ($e -is [UnauthorizedAccessException]) 'non-admin cannot publish'
    $e = Throws { $null = Call 'Releases' 'Publish' @($A, $v1exe, 'x', '', 'stable', $false, $null) }
    Assert ($e -ne $null) 'publishing straight to stable is refused'
    $rel1 = Call 'Releases' 'Publish' @($A, $v1exe, 'First release', 'abc123', 'pilot', $false, $null)
    Assert ($rel1.Sha -eq (Get-FileHash $v1exe -Algorithm SHA256).Hash.ToLower()) 'release published with checksum'
    $null = $A.Poll('updates'); $ann = $A.View('updates') | Where-Object { $_.M.Kind -eq 'release' }
    Assert (@($ann).Count -ge 1) 'release announced in Updates & Releases'
    $rel = T 'Releases'
    function Check($client, $dir) { $rel.GetField('MySha', $static).SetValue($null, ''); return (Call 'Releases' 'Check' @($client, $oldexe, $dir, $false)) }
    $bDir = Join-Path $fixture 'B-updates'; $cDir = Join-Path $fixture 'C-updates'
    $s = Check $B $bDir
    Assert ($s -eq 'up to date') 'stable PC gets nothing while the release is only on pilot'
    $bid = $B.Me; $cid = $C.Me
    $null = Call 'Releases' 'ChangeChannels' @($A, (Delegate $ChAction { param($x) $x.PilotDevices.Add($bid); $x.PilotDevices.Add($cid) }.GetNewClosure()), 'pilot')
    $s = Check $B $bDir
    Assert ($s -eq 'staged' -and (Get-FileHash (Join-Path $bDir "$($rel1.Version)\AllInOne.exe") -Algorithm SHA256).Hash.ToLower() -eq $rel1.Sha) 'pilot PC downloads and verifies the release'

    $mf = Join-Path $share "releases\$($rel1.Version)\manifest.json"; $orig = [IO.File]::ReadAllText($mf)
    [IO.File]::WriteAllText($mf, $orig.Replace('First release', 'Evil release'))
    $s = Check $C $cDir
    Assert ($s -eq 'failed' -and $rel.GetField('Error', $static).GetValue($null) -like '*rejected*') 'tampered manifest rejected'
    [IO.File]::WriteAllText($mf, $orig)
    $cj = Join-Path $share 'channels.json'; $cs = Join-Path $share 'channels.sig'
    $oj = [IO.File]::ReadAllText($cj); $os = [IO.File]::ReadAllText($cs)
    [IO.File]::WriteAllText($cj, $oj.Replace('"Paused":false', '"Paused":true'))
    $s = Check $C $cDir
    Assert ($s -eq 'failed') 'tampered channels rejected'
    [IO.File]::WriteAllText($cj, $oj)
    $null = Call 'Releases' 'ChangeChannels' @($A, (Delegate $ChAction { param($x) $x.Rollout = 100 }), 'touch')
    $null = Check $C $cDir
    [IO.File]::WriteAllText($cj, $oj); [IO.File]::WriteAllText($cs, $os)   # replay older signed channels
    $s = Check $C $cDir
    Assert ($s -eq 'failed' -and $rel.GetField('Error', $static).GetValue($null) -like '*replayed*') 'replayed old channel data rejected'
    $null = Call 'Releases' 'ChangeChannels' @($A, (Delegate $ChAction { param($x) $x.Rollout = 100 }), 'restore')

    $rel2 = Call 'Releases' 'Publish' @($A, $v2exe, 'Second release', 'def456', 'pilot', $false, $null)
    $null = Call 'Releases' 'ChangeChannels' @($A, (Delegate $ChAction { param($x) $x.Paused = $true }), 'pause')
    $s = Check $C $cDir
    Assert ($s -eq 'paused') 'paused deployment downloads nothing'
    $null = Call 'Releases' 'ChangeChannels' @($A, (Delegate $ChAction { param($x) $x.Paused = $false }), 'resume')
    $staged = Join-Path $cDir "$($rel2.Version)\AllInOne.exe"
    New-Item -ItemType Directory -Force (Split-Path $staged) | Out-Null
    $half = [IO.File]::ReadAllBytes($v2exe)[0..(1MB - 1)]; [IO.File]::WriteAllBytes($staged + '.part', [byte[]]$half)
    $s = Check $C $cDir
    Assert ($s -eq 'staged' -and (Get-FileHash $staged -Algorithm SHA256).Hash.ToLower() -eq $rel2.Sha) 'interrupted download resumed and verified'

    $relExe = Join-Path $share "releases\$($rel2.Version)\AllInOne.exe"; $good = [IO.File]::ReadAllBytes($relExe)
    $bad = [byte[]]$good.Clone(); $bad[5000] = $bad[5000] -bxor 1; [IO.File]::WriteAllBytes($relExe, $bad)
    $dDir = Join-Path $fixture 'D-updates'
    $s = Check $B $dDir
    Assert ($s -eq 'failed' -and -not (Test-Path (Join-Path $dDir "$($rel2.Version)\AllInOne.exe"))) 'corrupted release file refused and not staged'
    [IO.File]::WriteAllBytes($relExe, $good)

    Write-Host 'Simultaneous downloads (3 processes)'
    $jobs = 1..3 | ForEach-Object {
        $n = $_
        Start-Job -ArgumentList $exePath, $relExe, (Join-Path $fixture "par$n\AllInOne.exe") -ScriptBlock {
            param($asm, $src, $dst)
            $a = [Reflection.Assembly]::LoadFile($asm)
            $a.GetType('StickyPro.TeamClient').GetMethod('CopyResumable').Invoke($null, @($src, $dst, $null, $null))
        }
    }
    $hashes = $jobs | Wait-Job -Timeout 120 | Receive-Job
    $jobs | Remove-Job -Force
    Assert (@($hashes | Where-Object { $_ -eq $rel2.Sha }).Count -eq 3) 'three PCs downloading at once all get a verified copy'

    Write-Host 'Promotion guard and rollback'
    $v2 = $rel2.Version
    $FillType = [Func``2].MakeGenericType((T 'TeamDevice'), (T 'TeamDevice'))
    $C.FillState = Delegate $FillType { param($d) $d.State = 'failed'; $d.Target = $v2; return $d }.GetNewClosure()
    $C.Heartbeat($true)
    $e = Throws { Call 'Releases' 'Promote' @($A, $rel2.Version, 100) }
    Assert ($e -ne $null -and $e.Message -like '*failed*') 'failed pilot never reaches stable'
    $C.FillState = $null; $C.Heartbeat($true)
    Call 'Releases' 'Promote' @($A, $rel2.Version, 100)
    $err = $null; $ch = Call 'Releases' 'Channels' @($B, $err)
    Assert ($ch.Stable -eq $rel2.Version) 'tested pilot release promoted to stable'
    $back = Call 'Releases' 'Rollback' @($A, 'stable')
    $ch = Call 'Releases' 'Channels' @($B, $err)
    Assert ($back -eq $rel1.Version -and $ch.Stable -eq $rel1.Version) 'stable rolled back to the previous release'

    Write-Host 'Safe install: replace, health check, automatic rollback, data kept'
    $app = Join-Path $fixture 'app'; New-Item -ItemType Directory $app | Out-Null
    $target = Join-Path $app 'AllInOne.exe'; Copy-Item $oldexe $target
    New-Item -ItemType Directory (Join-Path $app 'data') | Out-Null
    $settings = Join-Path $app 'data\settings.json'; Set-Content $settings '{"keep":"me"}'
    $udir = Join-Path $fixture 'apply'
    $ProcFunc = [Func``3].MakeGenericType([string], [string], [Diagnostics.Process])
    $healthy = Delegate $ProcFunc { param($exe, $a) [IO.File]::WriteAllText((Join-Path $udir 'health-9.9.ok'), 'ok'); return $null }.GetNewClosure()
    $code = Call 'Releases' 'ApplyUpdate' @($v2exe, $target, 0, '9.9', $udir, $healthy, 3000)
    Assert ($code -eq 0 -and (Get-FileHash $target).Hash -eq (Get-FileHash $v2exe).Hash) 'update installed after the health signal'
    Assert ((Get-Content $settings -Raw).Trim() -eq '{"keep":"me"}') 'user data untouched by the update'
    Copy-Item $oldexe $target -Force
    $silent = Delegate $ProcFunc { param($exe, $a) return $null }
    $code = Call 'Releases' 'ApplyUpdate' @($v2exe, $target, 0, '9.8', $udir, $silent, 1500)
    Assert ($code -eq 1 -and (Get-FileHash $target).Hash -eq (Get-FileHash $oldexe).Hash) 'no health signal: previous version restored automatically'
    Assert ((Get-Content (Join-Path $udir 'result.json') -Raw) -like '*"ok":false*') 'failure recorded for the report'

    Write-Host 'Folder failures and admin commands'
    Rename-Item $share ($share + '-gone')
    $e = Throws { $null = Call 'Releases' 'Publish' @($A, $v1exe, 'x', '', 'pilot', $false, $null) }
    Assert ($e -ne $null) 'publish to an unreachable folder fails cleanly'
    $s = Check $B $bDir
    Assert ($s -eq 'offline') 'client reports offline instead of crashing'
    Rename-Item ($share + '-gone') $share
    Assert (@(Get-ChildItem (Join-Path $share 'releases') -Directory | Where-Object Name -like '_staging*').Count -eq 0) 'no half-published release left behind'
    $no = [Func[string, bool]] { param($q) $false }
    $ans = Call 'TeamAdmin' 'Handle' @($B, 'Show computers still using the old version', $no)
    Assert ($ans -like 'PCs not on*' -or $ans -like 'Every PC*') 'chat command lists PCs on old versions'
    $ans = Call 'TeamAdmin' 'Handle' @($B, '/pause', $no)
    Assert ($ans -like 'Only the team admin*') 'admin commands refused for members'
    $ans = Call 'TeamAdmin' 'Handle' @($A, '/rollback pilot', $no)
    Assert ($ans -eq 'Cancelled.') 'disruptive command needs confirmation'
    Assert ($A.Post('general', 'text', 'plain text', $null) -ne $null -and (Call 'TeamAdmin' 'Handle' @($A, 'hello team', $no)) -eq $null) 'normal messages are not commands'

    Write-Host "TEAM TESTS PASSED ($script:passed checks)"
}
finally {
    foreach ($x in $A, $B, $C) { try { Call 'Releases' 'DeleteKey' @($x) } catch {}; try { Call 'TeamSecurity' 'DeleteDeviceKey' @($x) } catch {} }
    Remove-Item -Recurse -Force $fixture -ErrorAction SilentlyContinue
}


