<#
.SYNOPSIS
  功能冒烟：设备信息字段、文件 mkdir/push/pull/rm、截屏、调试栈顶、应用列表。
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo 'docs\selftest'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$adb = Join-Path $repo 'tools\third-party\platform-tools\adb.exe'
if (-not (Test-Path $adb)) { $adb = 'F:\AndroidSdk\platform-tools\adb.exe' }
$serial = '99990000888800'
$report = Join-Path $out ("features-smoke-{0:yyyyMMdd-HHmmss}.md" -f (Get-Date))
$lines = [System.Collections.Generic.List[string]]::new()
function Add([string]$s) { $lines.Add($s) }
Add '# 功能冒烟'
Add ''
Add "时间: $(Get-Date -Format o)"
Add "adb: $adb"
Add "serial: $serial"
Add ''

function RunAdb([string[]]$args) {
  & $adb -s $serial @args 2>&1 | Out-String
}

Add '## 设备字段'
Add ('- model: ' + (RunAdb @('shell','getprop','ro.product.model')).Trim())
Add ('- patch: ' + (RunAdb @('shell','getprop','ro.build.version.security_patch')).Trim())
Add ('- size: ' + (RunAdb @('shell','wm','size')).Trim())
Add ''

Add '## 截屏'
$png = Join-Path $env:TEMP 'te-feature-screencap.png'
& $adb -s $serial exec-out screencap -p > $png
$ok = (Test-Path $png) -and ((Get-Item $png).Length -gt 1000)
Add ("- screencap: " + $(if($ok){'PASS'}else{'FAIL'}))
Add ''

Add '## 文件往返（中文+空格）'
$remoteDir = '/sdcard/Download/te-selftest'
$name = 'te 样本 file.txt'
$local = Join-Path $env:TEMP $name
Set-Content -Path $local -Value "hello-testengineer-$(Get-Date -Format o)" -Encoding UTF8
$null = RunAdb @('shell','mkdir','-p',$remoteDir)
$push = RunAdb @('push', $local, "$remoteDir/$name")
Add '```'
Add $push.Trim()
Add '```'
$ls = RunAdb @('shell','ls','-la',$remoteDir)
Add '```'
Add $ls.Trim()
Add '```'
$pullLocal = Join-Path $env:TEMP 'te-pulled.txt'
$pull = RunAdb @('pull', "$remoteDir/$name", $pullLocal)
Add ("- pull: " + $(if(Test-Path $pullLocal){'PASS'}else{'FAIL'}))
$rm = RunAdb @('shell','rm','-f',"$remoteDir/$name")
Add ("- cleanup: " + $rm.Trim())
Add ''

Add '## 第三方应用数'
$pkgs = (& $adb -s $serial shell pm list packages -3).Count
Add "- count: $pkgs"
Add ''

Add '## dumpsys activity (top excerpt)'
$act = RunAdb @('shell','dumpsys','activity','activities')
$excerpt = ($act -split "`n" | Select-String -Pattern 'ResumedActivity|mResumedActivity|topResumedActivity' | Select-Object -First 5)
Add '```'
$excerpt | ForEach-Object { Add $_.Line.Trim() }
Add '```'

Set-Content -Path $report -Value ($lines -join "`n") -Encoding UTF8
Write-Host "Wrote $report"
Get-Content $report
