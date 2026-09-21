<#
.SYNOPSIS
    一键复现构建（免管理员）。把本次精简重构踩过的环境坑全部固化下来。

.DESCRIPTION
    本仓库有 3 个非显然的构建前提，缺任一个都会失败，且报错信息不指向真正原因：

    1) 必须用 SDK 6.0.101（global.json 钉死）。
       若本机没有，本脚本会把 SDK 的 zip 直接解压到用户目录下一个独立文件夹
       （不写注册表、不改 PATH、不需要管理员）。

    2) references/reactive（Rx.NET）使用 Nerdbank.GitVersioning，需要【完整 git 历史】。
       浅克隆（git submodule update --depth 1）会导致构建期报
       "Unable to get version from commit ..." 这类看不懂的错误。
       另外 Rx.NET 里有个超长路径的 UWP 测试包，Windows 上需要 core.longpaths 才能检出。

    3) NuGet 还原会间歇报 "Access to the path ... is denied"。
       这是并行还原的竞态，加 --disable-parallel 即稳定通过（不是权限问题）。

    另：本仓库的 nuget.config 已移除失效的 AvaloniaCI 源（原返回 HTTP 521，
    会让 restore 直接失败），并显式加入 nuget.org —— 所以正常情况下无需再覆盖源。

.PARAMETER Configuration
    构建配置，默认 Debug。

.PARAMETER Test
    构建完成后额外运行单元测试。

.PARAMETER SkipSdkInstall
    跳过 SDK 检查/安装步骤（已自行配好环境时使用）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build.ps1
    powershell -ExecutionPolicy Bypass -File tools\build.ps1 -Configuration Release -Test
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $Test,

    [switch] $SkipSdkInstall
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sdkVersion = '6.0.101'
$sdkDir = Join-Path $env:USERPROFILE ".dotnet$($sdkVersion.Split('.')[0])"
$dotnet = Join-Path $sdkDir 'dotnet.exe'

function Write-Step([string] $text) {
    Write-Host ""
    Write-Host "==== $text ====" -ForegroundColor Cyan
}

function Test-Command([string] $exe) {
    $null -ne (Get-Command $exe -ErrorAction SilentlyContinue)
}

# ---------------------------------------------------------------- 1) SDK
Write-Step "检查 .NET SDK $sdkVersion"

if (-not (Test-Path $dotnet)) {
    if ($SkipSdkInstall) {
        throw "未找到 $dotnet 且指定了 -SkipSdkInstall，无法继续。"
    }

    Write-Host "未找到 SDK，开始下载并解压到 $sdkDir （免管理员，不改 PATH）"
    $zip = Join-Path $env:USERPROFILE "dotnet-sdk-$sdkVersion-win-x64.zip"
    $url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.zip"

    if (-not (Test-Path $zip)) {
        Write-Host "下载 $url"
        # 关掉进度条：$ProgressPreference 会显著拖慢 Invoke-WebRequest
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    }
    else {
        Write-Host "复用已下载的 $zip"
    }

    New-Item -ItemType Directory -Force -Path $sdkDir | Out-Null
    Write-Host "解压中（约 600MB，需要一会儿）..."
    Expand-Archive -LiteralPath $zip -DestinationPath $sdkDir -Force
}

if (-not (Test-Path $dotnet)) {
    throw "SDK 安装后仍未找到 $dotnet"
}

$env:DOTNET_ROOT = $sdkDir
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Write-Host "使用 SDK: $(& $dotnet --version)"

Push-Location $repoRoot
try {
    # ------------------------------------------------------------ 2) 子模块
    Write-Step "准备 git 子模块"

    # 长路径支持：Rx.NET 里有超出 MAX_PATH 的文件
    & git config core.longpaths true

    Write-Host "初始化子模块（首次会较慢）..."
    & git submodule update --init --recursive

    # 子模块自己的 config 不继承父仓库的 longpaths，需要单独设
    $reactive = Join-Path $repoRoot 'references/reactive'
    if (Test-Path $reactive) {
        & git -C $reactive config core.longpaths true
        & git -C $reactive checkout -- .

        # Nerdbank.GitVersioning 需要完整历史；浅克隆会让构建失败
        $isShallow = (& git -C $reactive rev-parse --is-shallow-repository) -eq 'true'
        if ($isShallow) {
            Write-Host "references/reactive 是浅克隆，拉取完整历史（NBGV 需要）..."
            & git -C $reactive fetch --unshallow --tags
        }
    }

    # ------------------------------------------------------------ 3) 构建
    $app = 'src/ST.Client.Desktop.Avalonia.App/ST.Client.Avalonia.App.csproj'

    Write-Step "还原并构建 $app ($Configuration)"
    # --disable-parallel 规避并行还原的 "Access to the path ... denied" 竞态
    & $dotnet restore $app --disable-parallel
    if ($LASTEXITCODE -ne 0) { throw "restore 失败" }

    & $dotnet build $app -c $Configuration --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw "构建失败" }

    Write-Host ""
    Write-Host "构建成功。" -ForegroundColor Green

    # ------------------------------------------------------------ 4) 测试
    if ($Test) {
        $tests = 'tests/ST.Client.UnitTest/ST.Client.UnitTest.csproj'
        Write-Step "运行单元测试 $tests"
        & $dotnet test $tests -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "单元测试失败" }
    }

    Write-Host ""
    Write-Host "全部完成。产物目录："
    Write-Host "  src/ST.Client.Desktop.Avalonia.App/bin/$Configuration/"
}
finally {
    Pop-Location
}
