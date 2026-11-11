# 设备信息网络/运行时间冒烟：对两台设备采集并写证据
$adb = 'E:\CCode\TestEngineer\tools\third-party\platform-tools\adb.exe'
$out = 'E:\CCode\TestEngineer\docs\selftest\device-net-dual.md'
$serials = @('99990000888800','KKSTBE045026')
$lines = @('# 设备网络 / 运行时间自测', '', "时间: $(Get-Date -Format o)", '', '| 设备 | 型号 | uptime | IP | SSID | MAC | 网络类型线索 |', '|---|---|---|---|---|---|---|')
foreach ($s in $serials) {
  $model = (& $adb -s $s shell getprop ro.product.model).Trim()
  $up = (& $adb -s $s shell cat /proc/uptime).Trim()
  $ip = (& $adb -s $s shell ip -f inet addr show wlan0 2>$null) -join ' '
  if ($ip -match 'inet\s+(\d+\.\d+\.\d+\.\d+)') { $ipV = $Matches[1] } else { $ipV = '—' }
  $mac = (& $adb -s $s shell cat /sys/class/net/wlan0/address 2>$null).Trim()
  if (-not $mac) { $mac = '—' }
  $wifi = (& $adb -s $s shell cmd wifi status 2>$null) -join ' '
  $ssid = '—'
  if ($wifi -match 'Wifi is connected to\s+"([^"]+)"') { $ssid = $Matches[1] }
  $conn = (& $adb -s $s shell dumpsys connectivity 2>$null | Select-String -Pattern 'NetworkAgentInfo|WIFI|MOBILE|TRANSPORT' | Select-Object -First 3) -join ' / '
  $lines += "| $s | $model | $up | $ipV | $ssid | $mac | $($conn -replace '\|','/') |"
}
$lines += ''
$lines += '说明：应用内 DeviceInfoService 解析同路径字段；运行时间由 ViewModel 每秒刷新。'
Set-Content $out ($lines -join "`n") -Encoding UTF8
Write-Output "Wrote $out"
Get-Content $out
