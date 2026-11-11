<#
.SYNOPSIS
    对已连接设备做基础 adb 能力冒烟（不依赖应用 UI）。
#>
[CmdletBinding()]
param(
    [string]$Adb = 'F:\AndroidSdk\platform-tools\adb.exe',
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo 'docs\selftest' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if (-not (Test-Path $Adb)) {
    $bundled = Join-Path $repo 'tools\third-party\platform-tools\adb.exe'
    if (Test-Path $bundled) { $Adb = $bundled } else { throw "adb not found: $Adb" }
}

$report = Join-Path $OutDir "adb-smoke-$(Get-Date -Format 'yyyyMMdd-HHmmss').md"
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# ADB 冒烟报告")
$lines.Add("")
$lines.Add("时间: $(Get-Date -Format o)")
$lines.Add("adb: $Adb")
$lines.Add("")

$devices = & $Adb devices -l
$lines.Add('## devices -l')
$lines.Add('```')
$devices | ForEach-Object { $lines.Add($_) }
$lines.Add('```')

$serials = @()
foreach ($line in $devices) {
    if ($line -match '^(\S+)\s+device\b') { $serials += $Matches[1] }
}

if ($serials.Count -eq 0) { throw '没有在线 device' }

foreach ($s in $serials) {
    $lines.Add("")
    $lines.Add("## 设备 $s")
    $model = (& $Adb -s $s shell getprop ro.product.model).Trim()
    $release = (& $Adb -s $s shell getprop ro.build.version.release).Trim()
    $type = (& $Adb -s $s shell getprop ro.build.type).Trim()
    $patch = (& $Adb -s $s shell getprop ro.build.version.security_patch).Trim()
    $size = (& $Adb -s $s shell wm size).Trim()
    $uptime = (& $Adb -s $s shell cat /proc/uptime).Trim()
    $lines.Add("- model: $model")
    $lines.Add("- android: $release / $type")
    $lines.Add("- security_patch: $patch")
    $lines.Add("- wm size: $size")
    $lines.Add("- uptime: $uptime")

    $tmp = Join-Path $env:TEMP "te-screencap-$s.png"
    & $Adb -s $s exec-out screencap -p > $tmp
    $ok = (Test-Path $tmp) -and ((Get-Item $tmp).Length -gt 1000)
    $lines.Add("- screencap: $(if($ok){'PASS'}else{'FAIL'}) ($tmp)")

    $pkgCount = (& $Adb -s $s shell pm list packages).Count
    $lines.Add("- packages: $pkgCount")

    $sd = & $Adb -s $s shell ls /sdcard/
    $lines.Add("- /sdcard entries: $($sd.Count)")
}

$lines.Add("")
$lines.Add('## scrcpy')
$scrcpy = Join-Path $repo 'tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3\scrcpy.exe'
$lines.Add("- path exists: $(Test-Path $scrcpy)")

Set-Content -Path $report -Value ($lines -join "`n") -Encoding UTF8
Write-Host "Wrote $report"
Get-Content $report
