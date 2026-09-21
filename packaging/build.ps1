<#
.SYNOPSIS
    按 RID 发布桌面应用（多平台产物）。

.DESCRIPTION
    使用 src/ST.Client.Desktop.Avalonia.App/Properties/PublishProfiles/ 下的发布配置逐个 RID 发布。

    与上游版本的差异（本次精简后重写）：
      上游此脚本在 -isPublish 模式下会先构建 src/ST.Tools.Publish（p.exe），
      再用它做「版本写入 / 产物汇总」两步，并要求环境变量 $env:Token（上游发布流水线凭证）。
      该工具工程与 Token 机制属于上游的发布/分发链路，已随精简一并移除，
      因此本脚本只保留**真正做发布**的那部分：逐 RID dotnet publish。

.PARAMETER Configuration
    构建配置，默认 Release。传 Debug 时自动使用 dev-* 发布配置（产物带调试信息）。

.PARAMETER Rid
    只发布指定的一个 RID（如 win-x64）。不传则发布全部目标。

.PARAMETER Hash
    发布完成后打印每个 RID 主程序集的 SHA256（便于自用归档校验）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File packaging\build.ps1
    powershell -ExecutionPolicy Bypass -File packaging\build.ps1 -Rid win-x64
    powershell -ExecutionPolicy Bypass -File packaging\build.ps1 -Configuration Debug -Rid win-x64 -Hash
#>
param(
    [string] $Configuration = 'Release',
    [string] $Rid,
    [switch] $Hash
)

$ErrorActionPreference = 'Stop'

$RootPath = Split-Path $PSScriptRoot -Parent
$proj_path = "$RootPath\src\ST.Client.Desktop.Avalonia.App\ST.Client.Avalonia.App.csproj"
$output_dir = "$RootPath\src\ST.Client.Desktop.Avalonia.App\bin\$Configuration\Publish"

# 全部目标 RID（对应 PublishProfiles 下的同名 .pubxml）
$all_rids = @('fd-win-x64', 'win-x64', 'osx-x64', 'osx-arm64', 'linux-x64', 'linux-arm64')

if ($Rid) {
    if ($all_rids -notcontains $Rid) {
        Write-Error "未知 RID '$Rid'。可选：$($all_rids -join ', ')"
        exit 1
    }
    $targets = @($Rid)
}
else {
    $targets = $all_rids
}

function Build-App {
    param([string] $rid)

    # Debug 配置使用 dev-* 发布配置
    $profile = $rid
    if ($Configuration -eq 'Debug') { $profile = "dev-$rid" }

    if ($rid.StartsWith('fd-')) {
        $publishDir = "$output_dir\FrameworkDependent\$rid"
    }
    else {
        $publishDir = "$output_dir\$rid"
    }

    Write-Host "==> Building $rid (profile: $profile)" -ForegroundColor Cyan
    Remove-Item $publishDir -Recurse -Force -Confirm:$false -ErrorAction Ignore

    & dotnet publish $proj_path -c $Configuration `
        -p:PublishProfile=$profile `
        -p:DeployOnBuild=true `
        -p:ExtraDefineConstants=$profile `
        --nologo

    if ($LASTEXITCODE) { exit $LASTEXITCODE }

    return $publishDir
}

$results = @()

foreach ($rid in $targets) {
    $dir = Build-App $rid
    $results += [pscustomobject]@{ Rid = $rid; PublishDir = $dir }
}

Write-Host ''
Write-Host '发布完成：' -ForegroundColor Green
$results | ForEach-Object { Write-Host ("  {0,-12} -> {1}" -f $_.Rid, $_.PublishDir) }

if ($Hash) {
    Write-Host ''
    Write-Host 'SHA256：'
    foreach ($r in $results) {
        # 主程序集名固定为 Steam++，与 csproj 的 AssemblyName 一致
        $main = Get-ChildItem -Path $r.PublishDir -Filter 'Steam++.*' -File -ErrorAction Ignore |
                Where-Object { $_.Extension -in '.dll', '.exe' } |
                Select-Object -First 1
        if ($main) {
            $h = (Get-FileHash $main.FullName -Algorithm SHA256).Hash
            Write-Host ("  {0,-12} {1}  {2}" -f $r.Rid, $h, $main.Name)
        }
        else {
            Write-Host ("  {0,-12} (未找到主程序集)" -f $r.Rid)
        }
    }
}
