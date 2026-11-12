# 导航七页并截图到 docs/selftest/screenshots/accept/，并用 PrintWindow 捕获（含 HWND 宿主时用状态栏校验投屏）
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
$code = @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class AccCap {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdcBlt, uint nFlags);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  public struct RECT { public int L,T,R,B; }
  public static void Save(IntPtr hwnd, string path) {
    ShowWindow(hwnd, 9); BringWindowToTop(hwnd); SetForegroundWindow(hwnd);
    System.Threading.Thread.Sleep(400);
    RECT r; GetWindowRect(hwnd, out r);
    int w = Math.Max(1, r.R-r.L); int h = Math.Max(1, r.B-r.T);
    using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
    using (Graphics g = Graphics.FromImage(bmp)) {
      IntPtr hdc = g.GetHdc();
      PrintWindow(hwnd, hdc, 2);
      g.ReleaseHdc(hdc);
      bmp.Save(path, ImageFormat.Png);
    }
  }
}
"@
Add-Type -ReferencedAssemblies ([Reflection.Assembly]::LoadWithPartialName('System.Drawing').Location) -TypeDefinition $code

function Invoke-Named([string]$name) {
  # 每次查找前杀掉 scrcpy，防止 HwndHost/重连再次拖垮 UIA
  Get-Process scrcpy -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 400
  $p = Get-Process TestEngineer -ErrorAction Stop | Select-Object -First 1
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
  $byName = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
  $byBtn = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $and = New-Object System.Windows.Automation.AndCondition($byName, $byBtn)
  $b = $null
  try {
    $b = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $and)
  } catch {
    Start-Sleep -Milliseconds 500
    try { $b = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byName) } catch { return $false }
  }
  if ($null -eq $b) {
    try { $b = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byName) } catch { return $false }
  }
  if ($null -eq $b) { return $false }
  $pat = $null
  if ($b.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke() }
  else { return $false }
  Start-Sleep -Milliseconds 900
  return $true
}

$dir = 'E:\CCode\TestEngineer\docs\selftest\screenshots\accept'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$hwnd = (Get-Process TestEngineer | Select-Object -First 1).MainWindowHandle

# 临时停掉 scrcpy，避免 HwndHost 拖垮 UI Automation 遍历
Get-Process scrcpy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$pages = [ordered]@{
  '设备信息'='01-device'
  '应用管理'='02-apps'
  '文件管理'='03-files'
  '调试指令'='04-debug'
  '控制台'='05-console'
  '远程投屏'='06-cast'
  '关于'='07-about'
}

foreach ($name in $pages.Keys) {
  $file = $pages[$name]
  if (Invoke-Named $name) {
    $path = Join-Path $dir "$file.png"
    [AccCap]::Save($hwnd, $path)
    Write-Output "OK $name -> $file.png ($((Get-Item $path).Length))"
  } else {
    Write-Output "MISS $name"
  }
}

# 恢复投屏再截
if (Invoke-Named '启动投屏') {
  Start-Sleep -Seconds 5
} elseif (Invoke-Named '启动 / 重连') {
  Start-Sleep -Seconds 5
}
[AccCap]::Save($hwnd, (Join-Path $dir '06-cast-running.png'))
Write-Output "OK cast-running scrcpy=$((Get-Process scrcpy -EA SilentlyContinue|Measure).Count)"

# 状态栏文本
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
function Find-Status($el, $depth) {
  if ($depth -gt 8) { return }
  try {
    $n = $el.Current.Name
    if ($n -match '在线 .*投屏') { Write-Output "STATUS: $n"; return }
  } catch {}
  $c = $walker.GetFirstChild($el)
  while ($null -ne $c) { Find-Status $c ($depth+1); $c = $walker.GetNextSibling($c) }
}
Find-Status $root 0
Write-Output DONE
