Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
$code = @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class WinCap {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  public struct RECT { public int L; public int T; public int R; public int B; }
  public static void Save(IntPtr hwnd, string path) {
    ShowWindow(hwnd, 9);
    SetForegroundWindow(hwnd);
    System.Threading.Thread.Sleep(300);
    RECT r;
    GetWindowRect(hwnd, out r);
    int w = Math.Max(1, r.R - r.L);
    int h = Math.Max(1, r.B - r.T);
    using (Bitmap bmp = new Bitmap(w, h))
    using (Graphics g = Graphics.FromImage(bmp)) {
      g.CopyFromScreen(r.L, r.T, 0, 0, new Size(w, h));
      bmp.Save(path, ImageFormat.Png);
    }
  }
}
"@
Add-Type -ReferencedAssemblies ([Reflection.Assembly]::LoadWithPartialName('System.Drawing').Location) -TypeDefinition $code

function Get-TeRoot {
  $p = Get-Process TestEngineer -ErrorAction Stop | Select-Object -First 1
  return [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
}

function Invoke-Named([string]$name) {
  $root = Get-TeRoot
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
  $b = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
  if ($null -eq $b) { return $false }
  $pat = $null
  if ($b.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) {
    $pat.Invoke()
  } elseif ($b.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) {
    $pat.Select()
  } else {
    return $false
  }
  Start-Sleep -Milliseconds 700
  return $true
}

function Capture-Theme([string]$outdir) {
  New-Item -ItemType Directory -Force -Path $outdir | Out-Null
  $hwnd = (Get-Process TestEngineer | Select-Object -First 1).MainWindowHandle
  $map = [ordered]@{
    '设备信息' = '01-device'
    '应用管理' = '02-apps'
    '文件管理' = '03-files'
    '调试指令' = '04-debug'
    '控制台' = '05-console'
    '远程投屏' = '06-cast'
    '关于' = '07-about'
  }
  foreach ($name in $map.Keys) {
    $file = $map[$name]
    if (Invoke-Named $name) {
      $path = Join-Path $outdir "$file.png"
      [WinCap]::Save($hwnd, $path)
      Write-Output "OK $name -> $outdir\$file.png ($((Get-Item $path).Length))"
    } else {
      Write-Output "MISS $name"
    }
  }
}

$base = 'E:\CCode\TestEngineer\docs\selftest\screenshots'
Write-Output '=== dark (current) ==='
Capture-Theme $base

Write-Output '=== switch light ==='
if (-not (Invoke-Named '关于')) { Write-Output 'MISS 关于'; throw 'nav about failed' }
if (-not (Invoke-Named '浅色主题')) { Write-Output 'MISS 浅色主题'; throw 'theme light failed' }
Start-Sleep -Milliseconds 500
Capture-Theme (Join-Path $base 'light')

Write-Output '=== restore dark ==='
if (-not (Invoke-Named '关于')) { Write-Output 'MISS 关于 restore' }
Invoke-Named '深色主题' | Out-Null
Write-Output 'DONE'
