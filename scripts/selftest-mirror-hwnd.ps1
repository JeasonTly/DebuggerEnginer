# 按 ScrcpyMirrorSession 同款参数拉起 scrcpy，验证窗口标题可被 EnumWindows 找到（嵌入前置条件）。
$ErrorActionPreference = 'Stop'
$serial = if ($args[0]) { $args[0] } else { '99990000888800' }
$repo = 'E:\CCode\TestEngineer'
$scrcpy = Join-Path $repo 'tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3\scrcpy.exe'
$adb = Join-Path $repo 'tools\third-party\platform-tools\adb.exe'
$title = "TestEngineer-$serial"
$recDir = Join-Path $repo 'docs\selftest\artifacts'
New-Item -ItemType Directory -Force -Path $recDir | Out-Null
$rec = Join-Path $recDir "mirror-record-$serial-$(Get-Date -Format yyyyMMdd-HHmmss).mp4"

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class TeWin {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public static IntPtr Find(string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, _) => {
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512);
      GetWindowText(h, sb, sb.Capacity);
      if (sb.ToString() == title) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@

$env:ADB = $adb
$p = Start-Process -FilePath $scrcpy -ArgumentList @(
  '--serial', $serial,
  '--window-title', $title,
  '--max-size', '1024',
  '--window-borderless',
  '--no-audio',
  '--record', $rec
) -WorkingDirectory (Split-Path $scrcpy) -PassThru -WindowStyle Normal

$hwnd = [IntPtr]::Zero
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Milliseconds 200
  $hwnd = [TeWin]::Find($title)
  if ($hwnd -ne [IntPtr]::Zero) { break }
  if ($p.HasExited) { break }
}

$found = $hwnd -ne [IntPtr]::Zero
# 录屏需运行一段时间，并尽量优雅结束以便 scrcpy flush mp4。
Start-Sleep -Seconds 8
if (-not $p.HasExited) {
  try { $p.CloseMainWindow() | Out-Null } catch {}
  if (-not $p.WaitForExit(5000)) { try { Stop-Process -Id $p.Id -Force } catch {} }
}
Start-Sleep -Seconds 1
$recOk = (Test-Path $rec) -and ((Get-Item $rec).Length -gt 1000)

$report = @"
# 投屏 HWND / 录屏冒烟

时间: $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK')
设备: $serial
标题: $title（与 ScrcpyMirrorSession 一致）
参数: --window-borderless --max-size 1024 --record

| 项 | 结果 |
|---|---|
| 窗口可枚举（嵌入前置） | $(if($found){'PASS'}else{'FAIL'}) hwnd=$hwnd |
| 录屏文件 | $(if($recOk){'PASS'}else{'FAIL'}) path=$rec size=$(if(Test-Path $rec){(Get-Item $rec).Length}else{0}) |
"@
$path = Join-Path $repo 'docs\selftest\mirror-hwnd-embed.md'
Set-Content -Path $path -Value $report -Encoding UTF8
Write-Output $report
if (-not $found) { exit 1 }
