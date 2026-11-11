# 自包含 win-x64 发布 + Portable ZIP（含 portable.flag）。Inno 可选。
param(
  [string]$Configuration = 'Release',
  [string]$Runtime = 'win-x64',
  [switch]$SkipZip,
  [switch]$SkipInno
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\TestEngineer.Desktop\TestEngineer.Desktop.csproj'
$outRoot = Join-Path $repo 'artifacts\publish'
$appDir = Join-Path $outRoot "TestEngineer-$Runtime"
$zipPath = Join-Path $outRoot "TestEngineer-$Runtime-portable.zip"
$iss = Join-Path $repo 'installer\TestEngineer.iss'
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

Write-Host "dotnet publish → $appDir"
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
dotnet publish $proj -c $Configuration -r $Runtime --self-contained true `
  -p:PublishSingleFile=false `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $appDir
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Set-Content -Path (Join-Path $appDir 'portable.flag') -Value "portable`n$(Get-Date -Format o)" -Encoding UTF8

$adb = Join-Path $appDir 'tools\third-party\platform-tools\adb.exe'
$scrcpy = Join-Path $appDir 'tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3\scrcpy.exe'
$jar = Join-Path $appDir 'tools\device-helper\device-helper.jar'
Write-Host "adb=$(Test-Path $adb) scrcpy=$(Test-Path $scrcpy) jar=$(Test-Path $jar)"

if (-not $SkipZip) {
  if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
  Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zipPath -Force
  Write-Host "ZIP: $zipPath"
  $sha = Get-FileHash $zipPath -Algorithm SHA256
  @"
ZIP=$zipPath
SHA256=$($sha.Hash)
adb=$(Test-Path $adb)
scrcpy=$(Test-Path $scrcpy)
jar=$(Test-Path $jar)
"@ | Set-Content (Join-Path $outRoot 'SHA256.txt') -Encoding UTF8
  Write-Host "SHA256=$($sha.Hash)"
}

$setupPath = $null
if (-not $SkipInno -and (Test-Path $iscc) -and (Test-Path $iss)) {
  Write-Host "ISCC → installer"
  & $iscc "/DMyAppSource=$appDir" $iss
  if ($LASTEXITCODE -ne 0) { throw "Inno compile failed" }
  $setupPath = Get-ChildItem (Join-Path $outRoot 'TestEngineer-Setup-*.exe') | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($setupPath) { Write-Host "SETUP: $($setupPath.FullName)" }
} else {
  Write-Host "Skip Inno (iscc=$(Test-Path $iscc) iss=$(Test-Path $iss) SkipInno=$SkipInno)"
}

@"
# 发布产物

时间: $(Get-Date -Format o)
- 目录: ``$appDir``
- Portable ZIP: ``$zipPath``
- Setup: $(if ($setupPath) { '`' + $setupPath.FullName + '`' } else { '（未生成）' })
- 随包: adb=$(Test-Path $adb) scrcpy=$(Test-Path $scrcpy) device-helper=$(Test-Path $jar)
"@ | Set-Content (Join-Path $repo 'docs\selftest\publish.md') -Encoding UTF8

Write-Host "DONE $appDir"
