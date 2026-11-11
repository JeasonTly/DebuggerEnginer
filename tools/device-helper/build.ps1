# 构建设备端辅助 jar（classes.dex 打包），供 adb push + app_process 使用。
# 依赖：JDK 8+（javac）、Android SDK platforms + build-tools（d8）。
param(
    [string]$AndroidSdk = $(if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } elseif ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "F:\AndroidSdk" }),
    [string]$Platform = "android-30"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$src = Join-Path $root "src"
$out = Join-Path $root "out"
$jar = Join-Path $root "device-helper.jar"

$androidJar = Join-Path $AndroidSdk "platforms\$Platform\android.jar"
if (-not (Test-Path $androidJar)) {
    $fallback = Get-ChildItem (Join-Path $AndroidSdk "platforms") -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "android.jar") } |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if (-not $fallback) { throw "未找到 android.jar，请设置 ANDROID_SDK_ROOT 或安装 platforms。" }
    $androidJar = Join-Path $fallback.FullName "android.jar"
    Write-Host "使用平台：$($fallback.Name)"
}

$d8 = Get-ChildItem (Join-Path $AndroidSdk "build-tools") -Directory |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName "d8.bat" } |
    Where-Object { Test-Path $_ } |
    Select-Object -First 1
if (-not $d8) { throw "未找到 d8.bat（build-tools）。" }

$javac = Get-Command javac -ErrorAction SilentlyContinue
if (-not $javac) { throw "未找到 javac。" }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null
$classes = Join-Path $out "classes"
$dexDir = Join-Path $out "dex"
New-Item -ItemType Directory -Path $classes, $dexDir | Out-Null

$javaFiles = Get-ChildItem $src -Filter *.java -Recurse | ForEach-Object { $_.FullName }
if (-not $javaFiles) { throw "无 Java 源文件。" }

Write-Host "javac → $classes"
$javacArgs = @(
    "-encoding", "UTF-8",
    "--release", "8",
    "-classpath", $androidJar,
    "-d", $classes
) + $javaFiles
& javac @javacArgs
if ($LASTEXITCODE -ne 0) { throw "javac 失败。" }

$classFiles = @(Get-ChildItem $classes -Filter *.class -Recurse | ForEach-Object { $_.FullName })
Write-Host "d8 → $dexDir"
$d8Args = @("--lib", $androidJar, "--min-api", "21", "--output", $dexDir) + $classFiles
& $d8 @d8Args
if ($LASTEXITCODE -ne 0) { throw "d8 失败。" }

$dex = Join-Path $dexDir "classes.dex"
if (-not (Test-Path $dex)) { throw "未生成 classes.dex。" }

if (Test-Path $jar) { Remove-Item $jar -Force }
Push-Location $dexDir
try {
    & jar cf $jar classes.dex
    if ($LASTEXITCODE -ne 0) { throw "jar 打包失败。" }
}
finally {
    Pop-Location
}

Write-Host "OK: $jar ($([math]::Round((Get-Item $jar).Length / 1KB, 1)) KB)"
