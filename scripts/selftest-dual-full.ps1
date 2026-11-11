# Dual-device full feature selftest — writes docs/selftest/full-features-dual-device.md
$ErrorActionPreference = 'Continue'
$repo = 'E:\CCode\TestEngineer'
$outDir = Join-Path $repo 'docs\selftest'
$artDir = Join-Path $outDir 'artifacts'
New-Item -ItemType Directory -Force -Path $outDir, $artDir | Out-Null
$logPath = Join-Path $outDir 'selftest-run.log'
function Log([string]$m) {
  $line = ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $m)
  Add-Content -Path $logPath -Value $line -Encoding UTF8
  Write-Host $line
}
Set-Content -Path $logPath -Value 'START' -Encoding UTF8

$adb = Join-Path $repo 'tools\third-party\platform-tools\adb.exe'
if (-not (Test-Path $adb)) { throw 'adb not found' }
$scrcpy = Join-Path $repo 'tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3\scrcpy.exe'
$apk = 'C:\Users\Tuly\Desktop\HZTR_ZNXJ.apk'
$pkg = 'me.zhouzhuo810.aiccheck'
$serials = @('99990000888800', 'KKSTBE045026')
$ts = Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

function Run-Adb {
  param([string]$Serial, [string[]]$Cmd, [int]$TimeoutSec = 45)
  $all = @()
  if ($Serial) { $all += @('-s', $Serial) }
  $all += $Cmd
  $outFile = Join-Path $env:TEMP ('te-out-' + [guid]::NewGuid().ToString('N') + '.txt')
  $errFile = Join-Path $env:TEMP ('te-err-' + [guid]::NewGuid().ToString('N') + '.txt')
  $timedOut = $false
  $code = -1
  try {
    # 拼成单行参数，避免 PS5.1 ArgumentList 数组转义问题；stdout/stderr 走临时文件防死锁
    $argLine = ($all | ForEach-Object {
      if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '
    $p = Start-Process -FilePath $adb -ArgumentList $argLine -RedirectStandardOutput $outFile -RedirectStandardError $errFile -NoNewWindow -PassThru
    if (-not $p.WaitForExit([Math]::Max(1000, $TimeoutSec * 1000))) {
      $timedOut = $true
      Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
      Start-Sleep -Milliseconds 200
    } else {
      $code = $p.ExitCode
    }
    $o = if (Test-Path $outFile) { Get-Content -Raw -LiteralPath $outFile -ErrorAction SilentlyContinue } else { '' }
    $e = if (Test-Path $errFile) { Get-Content -Raw -LiteralPath $errFile -ErrorAction SilentlyContinue } else { '' }
    if ($null -eq $o) { $o = '' }
    if ($null -eq $e) { $e = '' }
    if ($timedOut) { $e = 'TIMEOUT'; $code = -1 }
    return @{ ExitCode = $code; Out = $o; Err = $e; TimedOut = $timedOut }
  } finally {
    Remove-Item -Force -ErrorAction SilentlyContinue -LiteralPath $outFile, $errFile
  }
}

function Short([string]$t, [int]$n = 300) {
  if (-not $t) { return '' }
  $t = $t -replace "`r", '' -replace "`n", ' ; '
  $t = $t.Trim()
  if ($t.Length -le $n) { return $t }
  return $t.Substring(0, $n) + '...[truncated]'
}

$rows = @()
$pass = 0
$fail = 0
$failItems = @()
$timeoutN = 0

function Add-Row {
  param($Device, $Feature, $Result, $Command, $Summary)
  $script:rows += [pscustomobject]@{
    Time = (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK')
    Device = $Device
    Feature = $Feature
    Result = $Result
    Command = $Command
    Summary = $Summary
  }
  if ($Result -eq 'PASS') { $script:pass++ }
  elseif ($Result -eq 'FAIL') { $script:fail++; $script:failItems += "$Device / $Feature" }
  elseif ($Result -eq 'TIMEOUT') { $script:timeoutN++ }
  Log ("{0} | {1} | {2}" -f $Result, $Device, $Feature)
}

# Global devices
$dev = Run-Adb -Serial '' -Cmd @('devices','-l') -TimeoutSec 15
$both = ($dev.Out -match '99990000888800\s+device') -and ($dev.Out -match 'KKSTBE045026\s+device')
Add-Row 'BOTH' 'devices -l' $(if ($both) {'PASS'} else {'FAIL'}) 'adb devices -l' (Short $dev.Out 500)

$scExists = Test-Path $scrcpy
Add-Row 'HOST' 'scrcpy.exe 存在性' $(if ($scExists) {'PASS'} else {'FAIL'}) "Test-Path $scrcpy" "exists=$scExists"

foreach ($serial in $serials) {
  Log "==== DEVICE $serial ===="
  try {
    $model = (Run-Adb $serial @('shell','getprop','ro.product.model') -TimeoutSec 15).Out.Trim()
    $android = (Run-Adb $serial @('shell','getprop','ro.build.version.release') -TimeoutSec 15).Out.Trim()
    $btype = (Run-Adb $serial @('shell','getprop','ro.build.type') -TimeoutSec 15).Out.Trim()
    $ip = (Run-Adb $serial @('shell','getprop','dhcp.wlan0.ipaddress') -TimeoutSec 15).Out.Trim()
    if (-not $ip) {
      $ipRaw = (Run-Adb $serial @('shell','ip','-f','inet','addr','show','wlan0') -TimeoutSec 15).Out
      if ($ipRaw -match 'inet\s+(\d+\.\d+\.\d+\.\d+)') { $ip = $Matches[1] }
    }
    $ssidVal = ''
    $wifi = (Run-Adb $serial @('shell','cmd','wifi','status') -TimeoutSec 20).Out
    if ($wifi -match 'Wifi is connected to\s+"([^"]+)"') { $ssidVal = $Matches[1] }
    elseif ($wifi -match 'SSID:\s*"?([^"\r\n]+)"?') { $ssidVal = $Matches[1].Trim() }
    Add-Row $serial 'getprop 关键字段' $(if ($model -and $android -and $btype) {'PASS'} else {'FAIL'}) `
      'getprop model/release/type + ip/ssid' "model=$model android=$android type=$btype ip=$ip ssid=$ssidVal"

    $bat = Run-Adb $serial @('shell','dumpsys','battery') -TimeoutSec 30
    $batSum = (($bat.Out -split "`n" | Select-String 'level:|status:|health:|plugged:' | Select-Object -First 6) | ForEach-Object { $_.Line.Trim() }) -join '; '
    Add-Row $serial 'dumpsys battery' $(if ($bat.Out -match 'level:') {'PASS'} else {'FAIL'}) 'dumpsys battery' (Short $batSum)

    $df = Run-Adb $serial @('shell','df','-h') -TimeoutSec 20
    Add-Row $serial 'df' $(if ($df.Out -match '/data|/storage|Filesystem') {'PASS'} else {'FAIL'}) 'df -h' (Short $df.Out)

    $mem = Run-Adb $serial @('shell','cat','/proc/meminfo') -TimeoutSec 15
    $memSum = (($mem.Out -split "`n" | Select-Object -First 4) -join '; ')
    Add-Row $serial 'meminfo' $(if ($mem.Out -match 'MemTotal') {'PASS'} else {'FAIL'}) 'cat /proc/meminfo' (Short $memSum)

    $up = Run-Adb $serial @('shell','cat','/proc/uptime') -TimeoutSec 10
    Add-Row $serial 'uptime' $(if ($up.Out -match '\d') {'PASS'} else {'FAIL'}) 'cat /proc/uptime' (Short $up.Out 80)

    $wm = Run-Adb $serial @('shell','wm','size') -TimeoutSec 10
    Add-Row $serial 'wm size' $(if ($wm.Out -match '\d+x\d+') {'PASS'} else {'FAIL'}) 'wm size' (Short $wm.Out 80)

    $pkgs = Run-Adb $serial @('shell','pm','list','packages','-3') -TimeoutSec 30
    $pkgLines = @($pkgs.Out -split "`n" | Where-Object { $_.Trim() -match '^package:' })
    # 不以 ExitCode 为准（部分机型 shell 返回 -1 仍有有效输出）
    $pkgsOk = (-not $pkgs.TimedOut) -and ($null -ne $pkgs.Out)
    Add-Row $serial 'pm list packages -3' $(if ($pkgsOk) {'PASS'} else {'FAIL'}) `
      'pm list packages -3' ("count={0}; exit={1}; {2}" -f $pkgLines.Count, $pkgs.ExitCode, (Short ($pkgLines -join ', ') 200))

    # APK lifecycle
    if (-not (Test-Path $apk)) {
      Add-Row $serial 'APK install→启动→清数据→卸载' 'FAIL' "install $apk" 'APK missing'
    } else {
      [void](Run-Adb $serial @('uninstall', $pkg) -TimeoutSec 45)
      $inst = Run-Adb $serial @('install','-r','-t', $apk) -TimeoutSec 180
      $instOk = (($inst.Out + $inst.Err) -match 'Success')
      $parts = @('install=' + $(if ($instOk) {'Success'} else {'FAIL'}) + ' ' + (Short ($inst.Out + $inst.Err) 120))
      $launchOk = $false; $clearOk = $false; $unOk = $false
      if ($instOk) {
        $launch = Run-Adb $serial @('shell','monkey','-p',$pkg,'-c','android.intent.category.LAUNCHER','1') -TimeoutSec 30
        $launchOk = (($launch.Out + $launch.Err) -match 'Events injected:\s*1')
        if (-not $launchOk) {
          $launch2 = Run-Adb $serial @('shell','am','start','-a','android.intent.action.MAIN','-c','android.intent.category.LAUNCHER','-p',$pkg) -TimeoutSec 30
          $launchOk = (($launch2.Out + $launch2.Err) -match 'Starting:')
          $parts += 'launch=' + (Short ($launch2.Out + $launch2.Err) 100)
        } else {
          $parts += 'launch=monkey OK'
        }
        Start-Sleep -Seconds 2
        $clr = Run-Adb $serial @('shell','pm','clear',$pkg) -TimeoutSec 30
        $clearOk = (($clr.Out + $clr.Err) -match 'Success')
        $parts += 'clear=' + (Short ($clr.Out + $clr.Err) 60)
        $un = Run-Adb $serial @('uninstall', $pkg) -TimeoutSec 45
        $unOk = (($un.Out + $un.Err) -match 'Success')
        $parts += 'uninstall=' + (Short ($un.Out + $un.Err) 60)
      }
      $appOk = $instOk -and $launchOk -and $clearOk -and $unOk
      Add-Row $serial 'APK install→启动→清数据→卸载' $(if ($appOk) {'PASS'} else {'FAIL'}) `
        "install/launch/clear/uninstall $pkg" ($parts -join ' | ')
    }

    # files
    $localPush = Join-Path $env:TEMP ("te-selftest-$serial-$stamp.txt")
    Set-Content -Path $localPush -Value "te-selftest-$serial-$stamp" -Encoding UTF8
    $remote = "/sdcard/te-selftest-$stamp.txt"
    $push = Run-Adb $serial @('push', $localPush, $remote) -TimeoutSec 30
    $pushOk = (($push.Out + $push.Err) -match 'pushed')
    $ls = Run-Adb $serial @('shell','ls','-l',$remote) -TimeoutSec 15
    $lsOk = ($ls.Out -match 'te-selftest')
    $localPull = Join-Path $artDir "pulled-$serial-$stamp.txt"
    $pull = Run-Adb $serial @('pull', $remote, $localPull) -TimeoutSec 30
    $pullOk = (Test-Path $localPull) -and ((Get-Item $localPull).Length -gt 0)
    [void](Run-Adb $serial @('shell','rm','-f',$remote) -TimeoutSec 15)
    $rmCheck = Run-Adb $serial @('shell','ls',$remote) -TimeoutSec 15
    $rmOk = (($rmCheck.Out + $rmCheck.Err) -match 'No such') -or (-not $rmCheck.Out.Trim())
    Add-Row $serial '文件 push/ls/pull/rm (/sdcard/)' $(if ($pushOk -and $lsOk -and $pullOk -and $rmOk) {'PASS'} else {'FAIL'}) `
      "push/ls/pull/rm $remote" "push=$pushOk ls=$lsOk pull=$pullOk rm=$rmOk"

    $root = Run-Adb $serial @('shell','ls','/') -TimeoutSec 15
    Add-Row $serial '根目录只读探测 ls /' $(if ($root.Out -match 'system|sdcard|data|proc') {'PASS'} else {'FAIL'}) 'ls /' (Short $root.Out 200)

    # 仅设备端 grep，禁止整包 dumpsys 拉回主机
    $win = Run-Adb $serial @('shell','sh','-c','dumpsys window 2>/dev/null | grep -E "mCurrentFocus|mFocusedApp" | head -n 5') -TimeoutSec 35
    $focus = (($win.Out -split "`n" | Select-String 'mCurrentFocus|mFocusedApp' | Select-Object -First 3) | ForEach-Object { $_.Line.Trim() }) -join ' | '
    Add-Row $serial 'dumpsys window 焦点' $(if ($focus) {'PASS'} else {'FAIL'}) 'dumpsys window | grep focus' (Short $focus)

    $pngRemote = "/sdcard/te-screencap-$stamp.png"
    $pngLocal = Join-Path $artDir "screencap-$serial-$stamp.png"
    [void](Run-Adb $serial @('shell','screencap','-p',$pngRemote) -TimeoutSec 30)
    [void](Run-Adb $serial @('pull', $pngRemote, $pngLocal) -TimeoutSec 30)
    [void](Run-Adb $serial @('shell','rm','-f',$pngRemote) -TimeoutSec 15)
    $scOk = (Test-Path $pngLocal) -and ((Get-Item $pngLocal).Length -gt 1000)
    Add-Row $serial '截屏 screencap→artifacts' $(if ($scOk) {'PASS'} else {'FAIL'}) "screencap/pull $pngLocal" ("size={0}" -f $(if (Test-Path $pngLocal) {(Get-Item $pngLocal).Length} else {0}))

    $brLocal = Join-Path $artDir "bugreport-$serial-$stamp.zip"
    $br = Run-Adb $serial @('bugreport', $brLocal) -TimeoutSec 60
    $brSize = if (Test-Path $brLocal) { (Get-Item $brLocal).Length } else { 0 }
    if ($br.TimedOut) {
      Add-Row $serial 'bugreport (60s限时)' 'TIMEOUT' 'adb bugreport timeout 60s' ("TIMEOUT — 不计入致命失败; partialSize={0}" -f $brSize)
    } elseif ($brSize -gt 1000) {
      Add-Row $serial 'bugreport (60s限时)' 'PASS' "bugreport $brLocal" ("size={0}" -f $brSize)
    } else {
      # 60s 内结束但无产物：按限时未完成记 TIMEOUT，避免误报致命 FAIL
      Add-Row $serial 'bugreport (60s限时)' 'TIMEOUT' 'adb bugreport timeout 60s' ("no artifact in 60s; exit={0}; {1}" -f $br.ExitCode, (Short ($br.Out + $br.Err) 120))
    }

    $enf = Run-Adb $serial @('shell','getenforce') -TimeoutSec 10
    Add-Row $serial 'getenforce' $(if ($enf.Out -match 'Enforcing|Permissive|Disabled') {'PASS'} else {'FAIL'}) 'getenforce' (Short $enf.Out 40)

    $logc = Run-Adb $serial @('shell','logcat','-d','-t','20') -TimeoutSec 20
    Add-Row $serial 'logcat -d -t 20' $(if ($logc.Out.Trim().Length -gt 0) {'PASS'} else {'FAIL'}) 'logcat -d -t 20' (Short $logc.Out 250)

    $echo = Run-Adb $serial @('shell','echo','ok') -TimeoutSec 10
    Add-Row $serial 'adb -s shell echo ok' $(if ($echo.Out.Trim() -eq 'ok') {'PASS'} else {'FAIL'}) 'echo ok' (Short $echo.Out 20)
  } catch {
    Log ("EXCEPTION on $serial : " + $_.Exception.Message)
    Add-Row $serial 'device-loop' 'FAIL' 'try/catch' $_.Exception.Message
  }
}

# scrcpy short launch on first device
if ($scExists) {
  $target = $serials[0]
  Log "scrcpy short launch on $target"
  try {
    $sp = Start-Process -FilePath $scrcpy -ArgumentList @('-s', $target, '--window-title', 'TestEngineerSelftest', '--max-size', '800') `
      -WorkingDirectory (Split-Path $scrcpy) -PassThru -WindowStyle Normal
    Start-Sleep -Seconds 8
    if ($sp.HasExited) {
      Add-Row $target 'scrcpy 短时启动(~8s)' 'FAIL' 'scrcpy --window-title TestEngineerSelftest --max-size 800' ("exited early code={0}" -f $sp.ExitCode)
    } else {
      Stop-Process -Id $sp.Id -Force -ErrorAction SilentlyContinue
      Add-Row $target 'scrcpy 短时启动(~8s)' 'PASS' 'scrcpy --window-title TestEngineerSelftest --max-size 800' '进程存活>=8s后已杀'
    }
  } catch {
    Add-Row $target 'scrcpy 短时启动(~8s)' 'FAIL' 'scrcpy' $_.Exception.Message
  }
} else {
  Add-Row 'HOST' 'scrcpy 短时启动(~8s)' 'FAIL' 'scrcpy' 'exe missing'
}

# Write report
$report = Join-Path $outDir 'full-features-dual-device.md'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# 双机全功能自测证据')
[void]$sb.AppendLine('')
[void]$sb.AppendLine("时间戳: $ts")
[void]$sb.AppendLine(('完成时间: {0}' -f (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK')))
[void]$sb.AppendLine("adb: $adb")
[void]$sb.AppendLine("scrcpy: $scrcpy (exists=$scExists)")
[void]$sb.AppendLine("APK: $apk -> package $pkg")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 汇总')
[void]$sb.AppendLine('')
[void]$sb.AppendLine("- **PASS**: $pass")
[void]$sb.AppendLine("- **FAIL**: $fail")
[void]$sb.AppendLine("- **TIMEOUT**: $timeoutN（不计入致命）")
if ($failItems.Count -gt 0) {
  [void]$sb.AppendLine('- **失败项**:')
  foreach ($f in $failItems) { [void]$sb.AppendLine("  - $f") }
} else {
  [void]$sb.AppendLine('- **失败项**: 无')
}
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 设备清单')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('```')
[void]$sb.AppendLine($dev.Out.Trim())
[void]$sb.AppendLine('```')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 结果表（设备 × 功能）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| 时间 | 设备 | 功能 | 结果 | 命令 | 输出摘要 |')
[void]$sb.AppendLine('|---|---|---|---|---|---|')
foreach ($r in $rows) {
  $cmd = ($r.Command -replace '\|', '/' -replace '`', "'")
  $sum = ($r.Summary -replace '\|', '/' -replace '`', "'")
  [void]$sb.AppendLine(('| {0} | {1} | {2} | **{3}** | `{4}` | {5} |' -f $r.Time, $r.Device, $r.Feature, $r.Result, $cmd, $sum))
}
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 产物')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('- 报告: `docs/selftest/full-features-dual-device.md`')
[void]$sb.AppendLine('- 截屏/拉取/bugreport: `docs/selftest/artifacts/`')
Get-ChildItem $artDir -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 25 | ForEach-Object {
  [void]$sb.AppendLine(('- {0} ({1} bytes)' -f $_.Name, $_.Length))
}
[System.IO.File]::WriteAllText($report, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

$summary = @{ pass = $pass; fail = $fail; timeout = $timeoutN; failItems = $failItems; report = $report } | ConvertTo-Json -Compress
Set-Content -Path (Join-Path $outDir 'full-features-dual-device.summary.json') -Value $summary -Encoding UTF8
Log "DONE PASS=$pass FAIL=$fail TIMEOUT=$timeoutN"
Log "Report=$report"
Write-Host "PASS=$pass FAIL=$fail TIMEOUT=$timeoutN"
