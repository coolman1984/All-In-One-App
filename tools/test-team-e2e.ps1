# End-to-end check with the REAL app on a LOCAL test team folder (never S:\ or a production share).
#  1. Build & Publish pipeline (compile check, team tests, release build -Out, --selftest, signed publish).
#  2. A real app instance (portable copy in build-test\e2e\app, own data folder) joins the test team, gets a
#     forced pilot release, installs it through the updater, restarts and reports "updated".
# The normal All in One must be closed (single instance); this script closes it and starts it again at the end.
param([switch]$SkipPipeline, [string]$ShareRoot)   # -ShareRoot: a TEST subfolder on a real share, deleted at the end
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$root = Split-Path -Parent $PSScriptRoot
$built = Join-Path $root 'build-test\StickyNotesPro.test.exe'
if (-not (Test-Path $built)) { & (Join-Path $PSScriptRoot 'test-build.ps1') | Out-Null }
# Load a copy: the pipeline rebuilds build-test\StickyNotesPro.test.exe while this script runs.
$asmPath = Join-Path $env:TEMP ('aio-e2e-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.exe')
Copy-Item $built $asmPath
$assembly = [Reflection.Assembly]::LoadFile($asmPath)
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
function T([string]$n) { $assembly.GetType('StickyPro.' + $n) }
function Call([string]$type, [string]$method, [object[]]$a) {
    for ($i = 0; $i -lt $a.Count; $i++) { if ($null -ne $a[$i]) { $a[$i] = $a[$i].PSObject.BaseObject } }
    try { (T $type).GetMethod($method, $static).Invoke($null, $a) } catch [Reflection.TargetInvocationException] { throw $_.Exception.InnerException }
}
function Ok($v, $m) { if (-not $v) { throw "FAIL: $m" }; Write-Host "  ok  $m" }

$e2e = Join-Path $root 'build-test\e2e'
if (Test-Path $e2e) { Remove-Item -Recurse -Force $e2e }
$share = if ($ShareRoot) { $ShareRoot } else { Join-Path $e2e 'share' }; $app = Join-Path $e2e 'app'
New-Item -ItemType Directory -Force $share, $app, (Join-Path $app 'data') | Out-Null
(T 'Store').GetField('Dir', $static).SetValue($null, (Join-Path $e2e 'admin-data'))
$admin = [Activator]::CreateInstance((T 'TeamClient'), @([string](Join-Path $e2e 'admin')))
$admin.Local.Name = 'E2E admin'; $admin.Local.KeyContainer = 'AIO-E2E-' + [guid]::NewGuid().ToString('N'); $admin.Local.DeviceKeyContainer = 'AIO-E2EDev-' + [guid]::NewGuid().ToString('N'); $admin.SaveLocal()
$updates = Join-Path $env:LOCALAPPDATA 'AllInOne\updates'
try {
    $admin.Create($share, 'E2E team', 'e2e passphrase 123')
    $null = Call 'TeamSecurity' 'Unlock' @($admin, 'e2e passphrase 123')
    Ok $admin.IsAdmin 'test team created on a local folder'

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $st = Start-Process (Join-Path $root 'AllInOne.exe') '--selftest' -Wait -PassThru
    Ok ($st.ExitCode -eq 0) 'built app passes --selftest'

    if (-not $SkipPipeline) {
        Write-Host 'Build & Publish pipeline (takes a few minutes)...'
        $say = [Action[string]] { param($s) Write-Host "    $s" }
        $rel = Call 'TeamPublish' 'BuildAndPublish' @($admin, $root, 'dev', 'E2E pipeline release', $false, $false, $say)
        Ok ($rel.Version.Length -gt 0 -and (Test-Path (Join-Path $share "releases\$($rel.Version)\AllInOne.bin"))) "pipeline published $($rel.Version) in $([int]$sw.Elapsed.TotalSeconds) s"
    }

    Write-Host 'Real app update (about 3 minutes)...'
    Copy-Item (Join-Path $root 'AllInOne.exe') (Join-Path $app 'AllInOne.exe')
    Set-Content (Join-Path $app 'portable.txt') 'Test copy: never offer to install.'   # no install prompt, never touches the installed copy
    $oldSha = (Get-FileHash (Join-Path $app 'AllInOne.exe') -Algorithm SHA256).Hash.ToLower()
    # The test PC's data folder joins the team before the app starts.
    (T 'Store').GetField('Dir', $static).SetValue($null, (Join-Path $app 'data'))
    $pc = [Activator]::CreateInstance((T 'TeamClient'), @([string](Join-Path $app 'data\team')))
    $pc.Local.Name = 'E2E PC'; $pc.Join($share)
    $pcId = $pc.Me
    Call 'TeamSecurity' 'Approve' @($admin, $pcId)
    $chAction = [Action``1].MakeGenericType((T 'ReleaseChannels'))
    $addPilot = [Management.Automation.LanguagePrimitives]::ConvertTo({ param($x) $x.PilotDevices.Add($pcId) }.GetNewClosure(), $chAction)
    $null = Call 'Releases' 'ChangeChannels' @($admin, $addPilot, 'pilot')
    $newExe = $asmPath   # a different build of the same source = the "new version"
    $rel2 = Call 'Releases' 'Publish' @($admin, $newExe, 'E2E forced update', '', 'pilot', $true, $null)
    Remove-Item (Join-Path $updates 'result.json') -ErrorAction SilentlyContinue

    foreach ($p in Get-Process AllInOne -ErrorAction SilentlyContinue) { Start-Process $p.Path '--exit' -Wait; $p.WaitForExit(15000) | Out-Null }
    $proc = Start-Process (Join-Path $app 'AllInOne.exe') -PassThru
    $devFile = Join-Path $share ('devices\' + $pcId.ToLower() + '.json')
    $deadline = (Get-Date).AddMinutes(6); $seen = @()
    while ((Get-Date) -lt $deadline) {
        Start-Sleep 5
        if (Test-Path $devFile) {
            try { $raw = Get-Content $devFile -Raw | ConvertFrom-Json; if (-not $raw.data) { continue }; $d = $raw.data | ConvertFrom-Json } catch { continue }
            if ($d.AppPath -like '*AllInOne.exe' -and $d.AppPath -notlike "$app*") { throw "SAFETY STOP: the test app runs from $($d.AppPath), not from the test folder." }
            if ($seen -notcontains $d.State) { $seen += $d.State; Write-Host "    state: $($d.State) $($d.Error)" }
            if ($d.State -eq 'updated' -or $d.State -eq 'up to date' -and $d.Version -eq $rel2.Version) { break }
            if ($d.State -eq 'failed') { break }
        }
    }
    $newSha = (Get-FileHash (Join-Path $app 'AllInOne.exe') -Algorithm SHA256).Hash.ToLower()
    Ok ($newSha -eq $rel2.Sha -and $newSha -ne $oldSha) 'app exe replaced by the released version'
    Ok ($d.State -eq 'updated' -or $d.Version -eq $rel2.Version) "updated app restarted and reported (states: $($seen -join ' -> '))"
    Ok (Test-Path (Join-Path $app 'data\team\local.json')) 'app data kept across the update'
    Write-Host 'E2E PASSED'
}
finally {
    foreach ($p in Get-Process AllInOne -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$e2e*" }) { try { $p.Kill() } catch {} }
    try { Call 'Releases' 'DeleteKey' @($admin) } catch {}; try { Call 'TeamSecurity' 'DeleteDeviceKey' @($admin) } catch {}; if ($pc) { try { Call 'TeamSecurity' 'DeleteDeviceKey' @($pc) } catch {} }
    Start-Sleep 1
    if ($ShareRoot) { Remove-Item -Recurse -Force $ShareRoot -ErrorAction SilentlyContinue }
    if (-not (Get-Process AllInOne -ErrorAction SilentlyContinue)) { Start-Process (Join-Path $root 'AllInOne.exe') }
}


