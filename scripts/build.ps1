<#
.SYNOPSIS
    还原、编译、测试整个解决方案；任一步失败即以非零退出码结束。
.EXAMPLE
    powershell -File scripts/build.ps1
    powershell -File scripts/build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'TestEngineer.slnx'

function Invoke-Step {
    param([string]$Name, [string[]]$DotnetArgs)

    Write-Host "==> dotnet $($DotnetArgs -join ' ')" -ForegroundColor Cyan
    & dotnet @DotnetArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "步骤失败：$Name（退出码 $LASTEXITCODE）" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Push-Location $repoRoot
try {
    Invoke-Step 'restore' @('restore', $solution)
    Invoke-Step 'build' @('build', $solution, '-c', $Configuration, '--no-restore')
    if (-not $SkipTests) {
        Invoke-Step 'test' @('test', $solution, '-c', $Configuration, '--no-build')
    }
    Write-Host 'build.ps1 成功。' -ForegroundColor Green
}
finally {
    Pop-Location
}
