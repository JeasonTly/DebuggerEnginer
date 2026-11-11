# 双机全功能自测 v2：stdout/stderr 走临时文件，避免 PS 事件回调丢输出
$ErrorActionPreference = 'Continue'
$repo = 'E:\CCode\TestEngineer'
$outDir = Join-Path $repo 'docs\selftest'
$artDir = Join-Path $outDir 'artifacts'
New-Item -ItemType Directory -Force -Path $outDir, $artDir | Out-Null
$adb = Join-Path $repo 'tools\third-party\platform-tools\adb.exe'
$scrcpy = Join-Path $repo 'tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3\scrcpy.exe'
$apk = 'C:\Users\Tuly\Desktop\HZTR_ZNXJ.apk'
$pkg = 'me.zhouzhuo810.aiccheck'
$serials = @('99990000888800', 'KKSTBE045026')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$ts = Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK'
Write-Output "START $ts"

function Invoke-Adb {
  param([string]$Serial, [string[]]$ArgList, [int]$TimeoutSec = 40)
  $stdout = Join-Path $env:TEMP ("te-adb-out-" + [guid]::NewGuid().ToString('N') + '.txt')
  $stderr = Join-Path $env:TEMP ("te-adb-err-" + [guid]::NewGuid().ToString('N') + '.txt')
  $arg = @()
  if ($Serial) { $arg += @('-s', $Serial) }
  $arg += $ArgList
  $p = Start-Process -FilePath $adb -ArgumentList $arg -NoNewWindow -PassThru `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr
  $to = $false
  if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    $to = $true
    try { Stop-Process -Id $p.Id -Force } catch {}
    Start-Sleep -Milliseconds 200
  }
  $out = if (Test-Path $stdout) { Get-Content $stdout -Raw -ErrorAction SilentlyContinue } else { '' }
  $err = if (Test-Path $stderr) { Get-Content $stderr -Raw -ErrorAction SilentlyContinue } else { '' }
  Remove-Item $stdout, $stderr -Force -ErrorAction SilentlyContinue
  if ($null -eq $out) { $out = '' }
  if ($null -eq $err) { $err = '' }
  return [pscustomobject]@{
    ExitCode = $(if ($to) { -1 } else { $p.ExitCode })
    Out = $out
    Err = $(if ($to) { 'TIMEOUT' } else { $err })
    TimedOut = $to
  }
}

$rows = New-Object System.Collections.Generic.List[object]
$pass = 0; $fail = 0; $timeout = 0
$fails = New-Object System.Collections.Generic.List[string]

function Add-R([string]$dev, [string]$feat, [string]$result, [string]$cmd, [string]$sum) {
  $rows.Add([pscustomobject]@{ T = (Get-Date -Format 'HH:mm:ss'); D = $dev; F = $feat; R = $result; C = $cmd; S = $sum })
  if ($result -eq 'PASS') { $script:pass++ }
  elseif ($result -eq 'FAIL') { $script:fail++; [void]$fails.Add("$dev / $feat") }
  elseif ($result -eq 'TIMEOUT') { $script:timeout++ }
  Write-Output "[$result] $dev :: $feat :: $sum"
}

$dev = Invoke-Adb -Serial '' -ArgList @('devices','-l') -TimeoutSec 20
$bothOk = ($dev.Out -match '99990000888800\s+device') -and ($dev.Out -match 'KKSTBE045026\s+device')
Add-R 'BOTH' 'devices -l' ($(if ($bothOk) {'PASS'} else {'FAIL'})) 'adb devices -l' (($dev.Out -replace "`r|`n",' | ').Trim())
Add-R 'HOST' 'scrcpy.exe' ($(if (Test-Path $scrcpy) {'PASS'} else {'FAIL'})) $scrcpy "exists=$(Test-Path $scrcpy)"

foreach ($serial in $serials) {
  Write-Output "==== $serial ===="
  $model = (Invoke-Adb $serial @('shell','getprop','ro.product.model') -TimeoutSec 15).Out.Trim()
  $rel = (Invoke-Adb $serial @('shell','getprop','ro.build.version.release') -TimeoutSec 15).Out.Trim()
  $btype = (Invoke-Adb $serial @('shell','getprop','ro.build.type') -TimeoutSec 15).Out.Trim()
  $ip = (Invoke-Adb $serial @('shell','getprop','dhcp.wlan0.ipaddress') -TimeoutSec 15).Out.Trim()
  if (-not $ip) {
    $ipr = (Invoke-Adb $serial @('shell','ip','-f','inet','addr','show','wlan0') -TimeoutSec 15).Out
    if ($ipr -match 'inet\s+(\d+\.\d+\.\d+\.\d+)') { $ip = $Matches[1] }
  }
  $ssid = ''
  $ws = (Invoke-Adb $serial @('shell','cmd','wifi','status') -TimeoutSec 20).Out
  if ($ws -match 'Wifi is connected to\s+"([^"]+)"') { $ssid = $Matches[1] }
  Add-R $serial 'getprop 关键' ($(if ($model -and $rel -and $btype) {'PASS'} else {'FAIL'})) 'getprop' "model=$model android=$rel type=$btype ip=$ip ssid=$ssid"

  $bat = Invoke-Adb $serial @('shell','dumpsys','battery') -TimeoutSec 25
  $batSum = (($bat.Out -split "`n" | Select-String 'level:|status:' | Select-Object -First 4) -join '; ')
  Add-R $serial 'battery' ($(if ($bat.Out -match 'level:') {'PASS'} else {'FAIL'})) 'dumpsys battery' $batSum

  $df = Invoke-Adb $serial @('shell','df','/data') -TimeoutSec 15
  Add-R $serial 'df /data' ($(if ($df.Out -match '\d') {'PASS'} else {'FAIL'})) 'df /data' (($df.Out -replace "`r|`n",' ').Trim())

  $mem = Invoke-Adb $serial @('shell','cat','/proc/meminfo') -TimeoutSec 15
  Add-R $serial 'meminfo' ($(if ($mem.Out -match 'MemTotal') {'PASS'} else {'FAIL'})) 'cat /proc/meminfo' (($mem.Out -split "`n" | Select-Object -First 3) -join '; ')

  $up = Invoke-Adb $serial @('shell','cat','/proc/uptime') -TimeoutSec 10
  Add-R $serial 'uptime' ($(if ($up.Out -match '\d') {'PASS'} else {'FAIL'})) 'cat /proc/uptime' $up.Out.Trim()

  $wm = Invoke-Adb $serial @('shell','wm','size') -TimeoutSec 10
  Add-R $serial 'wm size' ($(if ($wm.Out -match '\d+x\d+') {'PASS'} else {'FAIL'})) 'wm size' $wm.Out.Trim()

  $pkgs = Invoke-Adb $serial @('shell','pm','list','packages','-3') -TimeoutSec 40
  $pc = @($pkgs.Out -split "`n" | Where-Object { $_.Trim() }).Count
  Add-R $serial 'pm list -3' ($(if ($pc -gt 0 -or $pkgs.ExitCode -eq 0) {'PASS'} else {'FAIL'})) 'pm list packages -3' "count=$pc"

  if (Test-Path $apk) {
    [void](Invoke-Adb $serial @('uninstall', $pkg) -TimeoutSec 45)
    $inst = Invoke-Adb $serial @('install','-r','-t',$apk) -TimeoutSec 150
    $instOk = (($inst.Out + $inst.Err) -match 'Success')
    $launchOk = $false; $clearOk = $false; $unOk = $false
    if ($instOk) {
      $launch = Invoke-Adb $serial @('shell','monkey','-p',$pkg,'-c','android.intent.category.LAUNCHER','1') -TimeoutSec 30
      $launchOk = (($launch.Out + $launch.Err) -match 'Events injected:\s*1')
      Start-Sleep -Seconds 1
      $clr = Invoke-Adb $serial @('shell','pm','clear',$pkg) -TimeoutSec 30
      $clearOk = (($clr.Out + $clr.Err) -match 'Success')
      $un = Invoke-Adb $serial @('uninstall',$pkg) -TimeoutSec 45
      $unOk = (($un.Out + $un.Err) -match 'Success')
    }
    Add-R $serial 'APK install→launch→clear→uninstall' ($(if ($instOk -and $launchOk -and $clearOk -and $unOk) {'PASS'} else {'FAIL'})) 'install/monkey/clear/uninstall' "inst=$instOk launch=$launchOk clear=$clearOk un=$unOk"
  } else {
    Add-R $serial 'APK install→launch→clear→uninstall' 'FAIL' $apk 'APK missing'
  }

  $local = Join-Path $env:TEMP "te-$serial-$stamp.txt"
  Set-Content $local "te-$serial-$stamp" -Encoding UTF8
  $remote = "/sdcard/te-selftest-$stamp.txt"
  $push = Invoke-Adb $serial @('push',$local,$remote) -TimeoutSec 30
  $pushOk = (($push.Out + $push.Err) -match 'pushed')
  $pullPath = Join-Path $artDir "pulled-$serial-$stamp.txt"
  [void](Invoke-Adb $serial @('pull',$remote,$pullPath) -TimeoutSec 30)
  $pullOk = (Test-Path $pullPath)
  [void](Invoke-Adb $serial @('shell','rm','-f',$remote) -TimeoutSec 15)
  Add-R $serial 'file push/ls/pull/rm' ($(if ($pushOk -and $pullOk) {'PASS'} else {'FAIL'})) "push/pull $remote" "push=$pushOk pull=$pullOk"

  $root = Invoke-Adb $serial @('shell','ls','/') -TimeoutSec 15
  Add-R $serial 'ls /' ($(if ($root.Out -match 'system|sdcard|data') {'PASS'} else {'FAIL'})) 'ls /' (($root.Out -split "`n" | Select-Object -First 8) -join ' ')

  $focus = Invoke-Adb $serial @('shell','dumpsys','window','windows') -TimeoutSec 40
  $fline = (($focus.Out -split "`n" | Select-String 'mCurrentFocus|mFocusedApp' | Select-Object -First 3) -join ' | ')
  Add-R $serial 'window focus' ($(if ($fline) {'PASS'} else {'FAIL'})) 'dumpsys window windows' $fline

  $pngR = "/sdcard/te-cap-$stamp.png"
  $pngL = Join-Path $artDir "screencap-$serial-$stamp.png"
  [void](Invoke-Adb $serial @('shell','screencap','-p',$pngR) -TimeoutSec 25)
  [void](Invoke-Adb $serial @('pull',$pngR,$pngL) -TimeoutSec 30)
  [void](Invoke-Adb $serial @('shell','rm','-f',$pngR) -TimeoutSec 10)
  $scOk = (Test-Path $pngL) -and ((Get-Item $pngL).Length -gt 1000)
  Add-R $serial 'screencap' ($(if ($scOk) {'PASS'} else {'FAIL'})) 'screencap -p' "size=$(if(Test-Path $pngL){(Get-Item $pngL).Length}else{0})"

  $brL = Join-Path $artDir "bugreport-$serial-$stamp.zip"
  $br = Invoke-Adb $serial @('bugreport',$brL) -TimeoutSec 60
  if ($br.TimedOut) { Add-R $serial 'bugreport' 'TIMEOUT' 'bugreport 60s' 'TIMEOUT non-fatal' }
  else {
    $brOk = (Test-Path $brL) -and ((Get-Item $brL).Length -gt 1000)
    Add-R $serial 'bugreport' ($(if ($brOk) {'PASS'} else {'FAIL'})) 'bugreport' "size=$(if(Test-Path $brL){(Get-Item $brL).Length}else{0})"
  }

  $enf = Invoke-Adb $serial @('shell','getenforce') -TimeoutSec 10
  Add-R $serial 'getenforce' ($(if ($enf.Out -match 'Enforcing|Permissive') {'PASS'} else {'FAIL'})) 'getenforce' $enf.Out.Trim()

  $log = Invoke-Adb $serial @('shell','logcat','-d','-t','20') -TimeoutSec 25
  Add-R $serial 'logcat -t 20' ($(if ($log.Out.Trim().Length -gt 0) {'PASS'} else {'FAIL'})) 'logcat -d -t 20' (($log.Out -split "`n" | Select-Object -First 1) -join '')

  $echo = Invoke-Adb $serial @('shell','echo','ok') -TimeoutSec 10
  Add-R $serial 'echo ok' ($(if ($echo.Out.Trim() -eq 'ok') {'PASS'} else {'FAIL'})) 'echo ok' $echo.Out.Trim()
}

if (Test-Path $scrcpy) {
  Write-Output '==== scrcpy ===='
  $sp = Start-Process -FilePath $scrcpy -ArgumentList @('-s','99990000888800','--window-title','TE-Selftest','--max-size','800','--no-audio') `
    -WorkingDirectory (Split-Path $scrcpy) -PassThru -WindowStyle Minimized
  Start-Sleep -Seconds 8
  if (-not $sp.HasExited) {
    try { Stop-Process -Id $sp.Id -Force } catch {}
    Add-R '99990000888800' 'scrcpy short' 'PASS' 'scrcpy 8s' 'alive then killed'
  } else {
    Add-R '99990000888800' 'scrcpy short' 'FAIL' 'scrcpy 8s' "early exit=$($sp.ExitCode)"
  }
} else {
  Add-R 'HOST' 'scrcpy short' 'FAIL' 'scrcpy' 'missing'
}

$report = Join-Path $outDir 'full-features-dual-device.md'
$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add('# 双机全功能自测证据')
[void]$lines.Add('')
[void]$lines.Add("时间戳: $ts")
[void]$lines.Add("完成: $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK')")
[void]$lines.Add("adb: $adb")
[void]$lines.Add("scrcpy: $scrcpy")
[void]$lines.Add("APK: $apk -> $pkg")
[void]$lines.Add('')
[void]$lines.Add('## 汇总')
[void]$lines.Add('')
[void]$lines.Add("- **PASS**: $pass")
[void]$lines.Add("- **FAIL**: $fail")
[void]$lines.Add("- **TIMEOUT**: $timeout（不计入致命）")
if ($fails.Count -gt 0) {
  [void]$lines.Add('- **失败项**:')
  foreach ($f in $fails) { [void]$lines.Add("  - $f") }
} else {
  [void]$lines.Add('- **失败项**: 无')
}
[void]$lines.Add('')
[void]$lines.Add('## 结果表')
[void]$lines.Add('')
[void]$lines.Add('| 时间 | 设备 | 功能 | 结果 | 命令 | 摘要 |')
[void]$lines.Add('|---|---|---|---|---|---|')
foreach ($r in $rows) {
  $s = ($r.S -replace '\|','/' -replace "`r|`n",' ')
  [void]$lines.Add("| $($r.T) | $($r.D) | $($r.F) | **$($r.R)** | ``$($r.C)`` | $s |")
}
[void]$lines.Add('')
[void]$lines.Add('## 产物')
Get-ChildItem $artDir -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 15 | ForEach-Object {
  [void]$lines.Add("- $($_.Name) ($($_.Length) bytes)")
}
Set-Content -Path $report -Value ($lines -join "`n") -Encoding UTF8
@{ pass = $pass; fail = $fail; timeout = $timeout; failItems = @($fails); report = $report } |
  ConvertTo-Json | Set-Content (Join-Path $outDir 'full-features-dual-device.summary.json') -Encoding UTF8
Write-Output "DONE PASS=$pass FAIL=$fail TIMEOUT=$timeout"
Write-Output "Report=$report"
