<#
.SYNOPSIS
    构建 DGLabVSCode for Visual Studio 2026。

.DESCRIPTION
    使用 Visual Studio 2026 自带的 MSBuild 还原 NuGet 包并生成 VSIX。
    可选执行不依赖 Visual Studio 的冒烟测试，或调用 VSIXInstaller 安装。

.PARAMETER Test
    构建并运行 tests/DGLabSmoke 冒烟测试（会真实连接一次郊狼服务器）。

.PARAMETER Install
    构建完成后调用 VSIXInstaller 安装到当前 Visual Studio 2026。

.PARAMETER Configuration
    构建配置，默认 Release。
#>
[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Install,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "src\DGLabVS2026\DGLabVS2026.csproj"
$testProject = Join-Path $root "tests\DGLabSmoke\DGLabSmoke.csproj"
$artifacts = Join-Path $root "artifacts"

function Get-VsWhere {
    $candidate = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $candidate) {
        return $candidate
    }

    throw "未找到 vswhere.exe，请安装 Visual Studio 2026。"
}

function Get-MSBuild {
    $vswhere = Get-VsWhere
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
        Select-Object -First 1
    if (-not $msbuild) {
        throw "未找到 MSBuild，请确认 Visual Studio 2026 安装了 .NET 桌面开发/扩展开发组件。"
    }

    return $msbuild
}

$msbuild = Get-MSBuild
Write-Host "MSBuild: $msbuild"

$vsix = Join-Path $root "src\DGLabVS2026\bin\$Configuration\net472\DGLabVS2026.vsix"
if (Test-Path $vsix) {
    # 强制重新打包，确保 README/第三方声明等内容变更进入 VSIX。
    Remove-Item $vsix -Force
}

Write-Host "构建插件..." -ForegroundColor Cyan
& $msbuild $project /restore "/p:Configuration=$Configuration" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) {
    throw "插件构建失败，退出码 $LASTEXITCODE。"
}

if (-not (Test-Path $vsix)) {
    throw "未找到生成的 VSIX: $vsix"
}

New-Item -ItemType Directory -Force $artifacts | Out-Null
$targetVsix = Join-Path $artifacts "DGLabVS2026.vsix"
Copy-Item $vsix $targetVsix -Force
Write-Host "VSIX: $targetVsix" -ForegroundColor Green

if ($Test) {
    Write-Host "构建冒烟测试..." -ForegroundColor Cyan
    & $msbuild $testProject /restore "/p:Configuration=$Configuration" /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) {
        throw "冒烟测试构建失败，退出码 $LASTEXITCODE。"
    }

    $testExe = Join-Path $root "tests\DGLabSmoke\bin\$Configuration\net472\DGLabSmoke.exe"
    Write-Host "运行冒烟测试..." -ForegroundColor Cyan
    & $testExe
    if ($LASTEXITCODE -ne 0) {
        throw "冒烟测试失败，退出码 $LASTEXITCODE。"
    }
}

if ($Install) {
    $vswhere = Get-VsWhere
    $installer = & $vswhere -latest -products * -find "Common7\IDE\VSIXInstaller.exe" |
        Select-Object -First 1
    if (-not $installer) {
        throw "未找到 VSIXInstaller.exe。"
    }

    Write-Host "安装到 Visual Studio 2026..." -ForegroundColor Cyan
    & $installer /quiet $targetVsix
    if ($LASTEXITCODE -ne 0) {
        throw "VSIX 安装失败，退出码 $LASTEXITCODE。"
    }
}

Write-Host "完成。" -ForegroundColor Green
