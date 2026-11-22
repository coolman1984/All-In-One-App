# Admin / user security tests: identities, approval, signed default-deny policy, encryption, revocation,
# suspend / block / terminate, admin authentication, and remote assistance (approval, view vs control,
# timeout, stop, revoke mid-session, forged files). Temporary folders and temporary keys only.
param([string]$ShareRoot, [string]$AssemblyPath)   # optional: a TEST subfolder on a real share (created and deleted by this script)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$root = Split-Path -Parent $PSScriptRoot
$built = if ($AssemblyPath) { (Resolve-Path -LiteralPath $AssemblyPath).Path } else { Join-Path $root 'build-test\StickyNotesPro.test.exe' }
if (-not (Test-Path $built)) { & (Join-Path $PSScriptRoot 'test-build.ps1') | Out-Null }
$assembly = [Reflection.Assembly]::LoadFile($built)
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$script:passed = 0
function Assert($value, [string]$message) { if (-not $value) { throw "FAIL: $message" }; $script:passed++; Write-Host "  ok  $message" }
function T([string]$name) { $assembly.GetType('StickyPro.' + $name) }
function Call([string]$type, [string]$method, [object[]]$a) {
    for ($i = 0; $i -lt $a.Count; $i++) { if ($null -ne $a[$i]) { $a[$i] = $a[$i].PSObject.BaseObject } }
    try { (T $type).GetMethod($method, $static).Invoke($null, $a) } catch [Reflection.TargetInvocationException] { throw $_.Exception.InnerException }
}
function Throws([scriptblock]$block) { try { & $block; return $null } catch { $x = $_.Exception; while (($x -is [Management.Automation.MethodInvocationException] -or $x -is [Reflection.TargetInvocationException]) -and $x.InnerException) { $x = $x.InnerException }; return $x } }
function Delegate($type, [scriptblock]$sb) { [Management.Automation.LanguagePrimitives]::ConvertTo($sb, $type) }
function Groups($c, $id, [string[]]$g) { $arr = New-Object 'object[]' 3; $arr[0] = $c; $arr[1] = $id; $arr[2] = $g; [void](T 'TeamSecurity').GetMethod('SetGroups', $static).Invoke($null, $arr) }
function SetStatic([string]$type, [string]$field, $value) { (T $type).GetField($field, $static).SetValue($null, $value) }

$fixture = Join-Path $root ('build-test\sec-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $fixture | Out-Null
SetStatic 'Store' 'Dir' $fixture
$share = if ($ShareRoot) { $ShareRoot } else { Join-Path $fixture 'share' }
$clients = @()
function Client([string]$name) {
    $c = [Activator]::CreateInstance((T 'TeamClient'), @([string](Join-Path $fixture $name)))
    $c.Local.Name = $name; $c.Local.KeyContainer = 'AIO-SecRel-' + [guid]::NewGuid().ToString('N'); $c.Local.DeviceKeyContainer = 'AIO-SecDev-' + [guid]::NewGuid().ToString('N'); $c.SaveLocal()
    $script:clients += $c
    return $c
}
function Refresh { foreach ($x in $script:clients) { $x.RefreshPolicy() } }
function Msgs($c, $room) { $null = $c.Poll($room); $p = $null; return @($c.Messages($room, [ref]$p)) }
$pass = 'correct horse battery'
$A = Client 'Admin'; $B = Client 'Bea'; $C = Client 'Cem'; $D = Client 'Dan'
SetStatic 'TeamRemote' 'ManualHosts' $true

try {
    Write-Host 'Admin authentication'
    $e = Throws { $A.Create($share, 'Sec', 'short') }
    Assert ($e -ne $null) 'admin passphrase must have 8+ characters'
    $A.Create($share, 'Sec', $pass)
    Assert (-not (Get-Content (Join-Path $fixture 'Admin\admin.json') -Raw).Contains($pass)) 'passphrase is not stored in clear'
    Assert (-not (Call 'TeamSecurity' 'Unlock' @($A, 'wrong passphrase'))) 'wrong passphrase refused'
    $e = Throws { Call 'TeamSecurity' 'Grant' @($A, '*', 'screen.view', '*', '') }
    Assert ($e -is [UnauthorizedAccessException]) 'admin actions need unlocking first'
    Assert (Call 'TeamSecurity' 'Unlock' @($A, $pass)) 'right passphrase unlocks admin mode'

    Write-Host 'Joining and approval'
    $B.Join($share); $C.Join($share); $D.Join($share)
    Refresh
    Assert ($D.MyStatus -eq 'pending' -and $D.Rooms().Count -eq 0) 'new PC waits for approval and sees no rooms'
    $joins = Call 'TeamSecurity' 'PendingJoins' @($A)
    $dj = @($joins | Where-Object Id -eq $D.Me)[0]
    Assert ((Call 'TeamSecurity' 'Fingerprint' @($dj.PublicKey)) -eq $D.DeviceCode) 'admin sees the same device code as the PC'
    $fake = @{ data = (@{ Id = 'evil'; Name = 'Evil'; PublicKey = $dj.PublicKey } | ConvertTo-Json -Compress); sig = 'AAAA' } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $share 'joins\evil.json'), $fake)
    Assert (@(Call 'TeamSecurity' 'PendingJoins' @($A) | Where-Object Id -eq 'evil').Count -eq 0) 'forged join request ignored'
    $e = Throws { Call 'TeamSecurity' 'Approve' @($B, $C.Me) }
    Assert ($e -is [UnauthorizedAccessException]) 'a normal user cannot approve PCs'
    Call 'TeamSecurity' 'Approve' @($A, $B.Me); Call 'TeamSecurity' 'Approve' @($A, $C.Me)
    Refresh
    Assert ($B.Approved -and @($B.Rooms() | ForEach-Object Id) -contains 'general') 'approved PC gets the default rooms'
    $epoch = Call 'TeamSecurity' 'LatestEpoch' @($A, 'general')
    Assert ($null -eq (Call 'TeamSecurity' 'RoomKey' @($D, 'general', $epoch))) 'pending PC holds no room key'

    Write-Host 'Failed join'
    $blockedShare = Join-Path $fixture 'blocked-share'
    New-Item -ItemType Directory -Path $blockedShare | Out-Null
    $teamInfo = Join-Path $blockedShare 'team.json'
    Set-Content -LiteralPath $teamInfo -Value '{"TeamId":"blocked-team","ReleaseKey":"blocked-key"}' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $blockedShare 'joins') -Value 'This file prevents writing a join request.'
    $newClient = Client 'New PC'
    foreach ($client in $newClient, $B) {
        $localFile = Join-Path $client.LocalDir 'local.json'
        $before = Get-Content -LiteralPath $localFile -Raw
        $activeShare = $client.Root
        $errorOnJoin = Throws { $client.Join($blockedShare) }
        Assert ($errorOnJoin -is [IO.IOException]) 'a failed shared write reaches the caller'
        Assert ($client.Root -eq $activeShare -and ($client.Configured -eq ($activeShare -ne ''))) 'failed join keeps the active membership'
        Assert ((Get-Content -LiteralPath $localFile -Raw) -ceq $before) 'failed join keeps the saved membership unchanged'
    }

    Write-Host 'Encryption and signatures'
    $secret = 'TOP-SECRET-' + [guid]::NewGuid().ToString('N')
    $null = $A.Post('general', 'text', $secret, $null)
    Assert (@(Msgs $B 'general' | Where-Object Text -eq $secret).Count -eq 1) 'member reads the message'
    $sfile = Join-Path $fixture 'plain.txt'; Set-Content $sfile ('FILE-' + $secret)
    $null = $A.SendFile('general', $sfile, $null, $null, $null)
    $leak = Get-ChildItem $share -Recurse -File | Where-Object { [IO.File]::ReadAllText($_.FullName).Contains($secret) }
    Assert (@($leak).Count -eq 0) 'no message or file text readable in the shared folder'
    $line = (Get-Content (Join-Path $share ('rooms\general\msgs\' + $A.Me.ToLower() + '.jsonl')))[0] | ConvertFrom-Json
    $line.Id = [guid]::NewGuid().ToString('N'); $line.From = $B.Me
    Add-Content (Join-Path $share 'rooms\general\msgs\forged.jsonl') ($line | ConvertTo-Json -Compress)
    $before = $C.Dropped
    $null = Msgs $C 'general'
    Assert ($C.Dropped -gt $before -and $C.LastDrop -like 'bad signature*') 'message with a forged sender is dropped'
    $D.Local.Outbox.Clear(); $null = $D.Post('general', 'text', 'from a pending PC', $null)
    Assert ($D.Local.Outbox.Count -eq 1) 'pending PC cannot send (message stays local)'

    Write-Host 'Default deny and permissions'
    $p = $A.Policy
    Assert (-not (Call 'TeamSecurity' 'Allowed' @($p, $B.Me, 'screen.view', $C.Me))) 'screen viewing is denied by default'
    Assert (Call 'TeamSecurity' 'Allowed' @($p, $A.Me, 'screen.control', $B.Me)) 'admin may do everything'
    Groups $A $B.Me @('eng')
    $room = $A.NewGroup('Secret plans', '')
    Call 'TeamSecurity' 'Grant' @($A, 'group:eng', 'room', ('room:' + $room.Id), 'test')
    Refresh
    Assert (@($B.Rooms($true) | Where-Object Id -eq $room.Id).Count -eq 1 -and @($C.Rooms($true) | Where-Object Id -eq $room.Id).Count -eq 0) 'room visible only to the granted group'
    $re = Call 'TeamSecurity' 'LatestEpoch' @($A, $room.Id)
    Assert ($null -ne (Call 'TeamSecurity' 'RoomKey' @($B, $room.Id, $re)) -and $null -eq (Call 'TeamSecurity' 'RoomKey' @($C, $room.Id, $re))) 'only granted members get the room key'
    $null = $B.Post($room.Id, 'text', 'eng only', $null)
    Assert (@(Msgs $A $room.Id | Where-Object Text -eq 'eng only').Count -eq 1) 'granted member writes in the room'
    $C.Local.Outbox.Clear(); $null = $C.Post($room.Id, 'text', 'intruder', $null)
    Assert ($C.Local.Outbox.Count -eq 1 -and @(Msgs $A $room.Id | Where-Object Text -eq 'intruder').Count -eq 0) 'non-member cannot write in the room'
    $C.Local.Outbox.Clear(); $C.SaveLocal()
    Assert (-not (Call 'TeamSecurity' 'Allowed' @($A.Policy, $B.Me, 'project', 'project:D:\Work\Proj\Sub'))) 'project access denied without a grant'
    Call 'TeamSecurity' 'Grant' @($A, $B.Me, 'project', 'project:D:\Work\Proj', '')
    Assert (Call 'TeamSecurity' 'Allowed' @($A.Policy, $B.Me, 'project', 'project:D:\Work\Proj\Sub')) 'project grant covers its subfolders'

    Write-Host 'Policy integrity'
    $pj = Join-Path $share 'policy.json'; $ps = Join-Path $share 'policy.sig'
    $oj = [IO.File]::ReadAllText($pj); $os = [IO.File]::ReadAllText($ps)
    [IO.File]::WriteAllText($pj, $oj.Replace('"Status":"pending"', '"Status":"approved"').Replace('"SessionMinutes":30', '"SessionMinutes":999'))
    $err = $null; $r = Call 'TeamSecurity' 'Load' @($C, $err)
    Assert ($null -eq $r) 'tampered policy rejected'
    [IO.File]::WriteAllText($pj, $oj)
    Call 'TeamSecurity' 'Grant' @($A, '*', 'files', 'room:general', 'bump')
    $C.RefreshPolicy()
    [IO.File]::WriteAllText($pj, $oj); [IO.File]::WriteAllText($ps, $os)
    $r = Call 'TeamSecurity' 'Load' @($C, $err)
    Assert ($null -eq $r) 'replayed old policy rejected'
    Call 'TeamSecurity' 'Revoke' @($A, '*', 'files', 'room:general')
    Refresh
    Assert ($C.Policy -ne $null -and $C.Policy.Seq -gt 7) 'admin repairs a replayed policy with the next change'

    Write-Host 'Suspend, revoke, block, terminate'
    Call 'TeamSecurity' 'SetStatus' @($A, $C.Me, 'suspended', $false); Refresh; $C.CheckMyAccess()
    $C.Local.Outbox.Clear(); $null = $C.Post('general', 'text', 'while suspended', $null)
    Assert ($C.Rooms().Count -eq 0 -and $C.Flush() -eq 0) 'suspended PC can neither read rooms nor send'
    Call 'TeamSecurity' 'SetStatus' @($A, $C.Me, 'approved', $false); Refresh
    Assert ($C.Flush() -ge 1) 'restored PC sends its queued message'
    $g1 = Call 'TeamSecurity' 'LatestEpoch' @($A, 'general')
    Call 'TeamSecurity' 'SetStatus' @($A, $B.Me, 'revoked', $false); Refresh; $B.CheckMyAccess()
    $g2 = Call 'TeamSecurity' 'LatestEpoch' @($A, 'general')
    Assert ($g2 -gt $g1) 'revoking rotates the room keys'
    $after = 'AFTER-REVOKE-' + [guid]::NewGuid().ToString('N')
    $null = $A.Post('general', 'text', $after, $null)
    Assert ($null -eq (Call 'TeamSecurity' 'RoomKey' @($B, 'general', $g2)) -and @(Msgs $B 'general' | Where-Object Text -eq $after).Count -eq 0) 'revoked PC cannot read new messages'
    $B.KeyCache.Clear(); $B.Local.Outbox.Clear(); $null = $B.Post('general', 'text', 'from revoked', $null)
    Assert (@(Msgs $A 'general' | Where-Object Text -eq 'from revoked').Count -eq 0) 'messages from a revoked PC are refused'
    $h0 = $C.Local.HandledTerminate
    Call 'TeamSecurity' 'SetStatus' @($A, $C.Me, 'approved', $true); Refresh; $C.CheckMyAccess()
    Assert ($C.Local.HandledTerminate -eq $h0 + 1) 'terminate order reaches the PC'
    Call 'TeamSecurity' 'SetStatus' @($A, $C.Me, 'blocked', $true); Refresh; $C.CheckMyAccess()
    Assert ($C.MyStatus -eq 'blocked' -and $C.Rooms().Count -eq 0 -and $C.Local.HandledTerminate -eq $h0 + 2) 'blocked PC is closed and stays disconnected'
    $e = Throws { Call 'TeamSecurity' 'SetStatus' @($A, $A.Me, 'blocked', $false) }
    Assert ($e -ne $null) 'admin cannot lock themselves out'
    Call 'TeamSecurity' 'SetStatus' @($A, $B.Me, 'approved', $false); Call 'TeamSecurity' 'SetStatus' @($A, $C.Me, 'approved', $false); Refresh
    Assert ($B.Approved -and $C.Approved) 'access can be given back'

    Write-Host 'Remote assistance'
    $frames = New-Object Collections.ArrayList; $inputs = New-Object Collections.ArrayList
    $fake = [Text.Encoding]::ASCII.GetBytes('FAKE-JPEG-' + [guid]::NewGuid().ToString('N'))
    SetStatic 'TeamRemote' 'Capture' (Delegate ([Func[byte[]]]) { return $fake }.GetNewClosure())
    $InType = [Action``1].MakeGenericType((T 'RemoteInput'))
    SetStatic 'TeamRemote' 'Inject' (Delegate $InType { param($e) [void]$inputs.Add($e) }.GetNewClosure())
    $e = Throws { $null = Call 'TeamRemote' 'Ask' @($B, $C.Me, 'view', '') }
    Assert ($e -is [UnauthorizedAccessException]) 'viewing a screen without permission is refused'
    Groups $A $B.Me @('eng', 'helpers')
    Call 'TeamSecurity' 'Grant' @($A, 'group:helpers', 'screen.control', '*', 'IT help')
    Refresh
    $req = Call 'TeamRemote' 'Ask' @($B, $C.Me, 'control', 'printer problem')
    $viewer = [Activator]::CreateInstance((T 'TeamRemote+Viewer'), @($B, $req))
    Assert (-not $viewer.Ready()) 'nothing is shared before the person approves'
    $s = @(Call 'TeamRemote' 'Sessions' @($C, 1) | Where-Object { $_.R.Sid -eq $req.Sid })[0]
    $e = Throws { $null = Call 'TeamRemote' 'Approve' @($B, $s.R, 'control') }
    Assert ($e -ne $null) 'only the person whose screen it is can approve'
    $host1 = Call 'TeamRemote' 'Approve' @($C, $s.R, 'view')
    Assert ($viewer.Ready() -and $viewer.A.Mode -eq 'view') 'approved as view only (lower than asked)'
    $null = $host1.Step($true)
    $got = $viewer.Frame()
    Assert ($got -ne $null -and [Text.Encoding]::ASCII.GetString($got) -eq [Text.Encoding]::ASCII.GetString($fake)) 'helper sees the screen frames'
    Assert (-not ([IO.File]::ReadAllText((Join-Path $share "sessions\$($req.Sid)\frame.bin"))).Contains('FAKE-JPEG')) 'frames are encrypted in the shared folder'
    $in = [Activator]::CreateInstance((T 'RemoteInput')); $in.Kind = 'down'; $in.X = 0.5; $in.Y = 0.5
    $viewer.Send($in); $null = $host1.Step($false)
    Add-Content (Join-Path $share "sessions\$($req.Sid)\input.jsonl") 'Zm9yZ2VkIGlucHV0'
    $null = $host1.Step($false)
    Assert ($inputs.Count -eq 0 -and $host1.Rejected -ge 1) 'view-only session never applies input (forged input rejected)'
    $viewer.Stop('done')
    Assert (-not $host1.Step($true) -and $host1.StopReason -like '*helper*') 'helper can stop the session'

    $req2 = Call 'TeamRemote' 'Ask' @($B, $C.Me, 'control', '')
    $s2 = @(Call 'TeamRemote' 'Sessions' @($C, 1) | Where-Object { $_.R.Sid -eq $req2.Sid })[0]
    $host2 = Call 'TeamRemote' 'Approve' @($C, $s2.R, 'control')
    $v2 = [Activator]::CreateInstance((T 'TeamRemote+Viewer'), @($B, $req2)); $null = $v2.Ready()
    $in = [Activator]::CreateInstance((T 'RemoteInput')); $in.Kind = 'move'; $in.X = 0.25; $in.Y = 0.75
    $v2.Send($in); $null = $host2.Step($false)
    Assert ($inputs.Count -eq 1 -and $inputs[0].X -eq 0.25 -and $inputs[0].Y -eq 0.75) 'control session applies mouse input'
    $last = (Get-Content (Join-Path $share "sessions\$($req2.Sid)\input.jsonl"))[-1]
    Add-Content (Join-Path $share "sessions\$($req2.Sid)\input.jsonl") $last
    $null = $host2.Step($false)
    Assert ($inputs.Count -eq 1) 'replayed input is ignored'
    Call 'TeamSecurity' 'Revoke' @($A, 'group:helpers', 'screen.control', '*'); Refresh
    Assert (-not $host2.Step($true) -and $host2.StopReason -like '*permission removed*') 'removing the permission ends a running session'

    Call 'TeamSecurity' 'Grant' @($A, 'group:helpers', 'screen.view', '*', ''); Refresh
    $req3 = Call 'TeamRemote' 'Ask' @($B, $C.Me, 'view', '')
    $s3 = @(Call 'TeamRemote' 'Sessions' @($C, 1) | Where-Object { $_.R.Sid -eq $req3.Sid })[0]
    $host3 = Call 'TeamRemote' 'Approve' @($C, $s3.R, 'view')
    $host3.A.Expires = 0
    Assert (-not $host3.Step($true) -and $host3.StopReason -like '*time*') 'session ends automatically at the time limit'
    $req4 = Call 'TeamRemote' 'Ask' @($B, $C.Me, 'view', '')
    $s4 = @(Call 'TeamRemote' 'Sessions' @($C, 1) | Where-Object { $_.R.Sid -eq $req4.Sid })[0]
    $host4 = Call 'TeamRemote' 'Approve' @($C, $s4.R, 'view')
    $evilEnd = @{ data = (@{ Sid = $req4.Sid; By = $D.Me; Reason = 'x' } | ConvertTo-Json -Compress); sig = 'AAAA' } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $share "sessions\$($req4.Sid)\end.json"), $evilEnd)
    Assert ($host4.Step($true)) 'forged stop file from an outsider is ignored'
    Call 'TeamRemote' 'End' @($A, $req4.Sid, 'ended by the admin')
    Assert (-not $host4.Step($true) -and $host4.StopReason -like '*admin*') 'admin can end any session'
    $offer = Call 'TeamRemote' 'Offer' @($C, $B.Me, 'view')
    Assert ($offer.Running -and $offer.A.Mode -eq 'view') 'user starts one-click help (sharing is their approval)'
    $offer.Stop('stopped by the person sharing')
    $e = Throws { $null = Call 'TeamRemote' 'Offer' @($C, $D.Me, 'view') }
    Assert ($e -ne $null) 'cannot share with someone the policy does not allow'
    $jpg = Call 'TeamRemote' 'CaptureScreen' @()
    Assert ($jpg.Length -gt 1000 -and $jpg[0] -eq 0xFF -and $jpg[1] -eq 0xD8) 'real Windows screen capture produces a JPEG'

    Write-Host 'Audit'
    $audit = (Get-ChildItem (Join-Path $share 'audit') -Filter *.jsonl | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    foreach ($act in 'admin-unlock-failed', 'policy', 'key-rotate', 'terminated', 'remote-ask', 'remote-approve', 'remote-stop', 'remote-end', 'denied') {
        Assert ($audit.Contains('"' + $act + '"')) "audit records $act"
    }
    Write-Host "SECURITY TESTS PASSED ($script:passed checks)"
}
finally {
    foreach ($x in $clients) { try { Call 'Releases' 'DeleteKey' @($x) } catch {}; try { Call 'TeamSecurity' 'DeleteDeviceKey' @($x) } catch {} }
    Remove-Item -Recurse -Force $fixture -ErrorAction SilentlyContinue
    if ($ShareRoot) { Remove-Item -Recurse -Force $ShareRoot -ErrorAction SilentlyContinue }
}





