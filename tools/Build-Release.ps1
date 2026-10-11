# 构建并准备 MCHPvPRotation 发布产物
#
# 用法:
#   .\tools\Build-Release.ps1                 # 构建，输出 dist\latest.zip
#   .\tools\Build-Release.ps1 -BumpVersion    # 顺带把版本号 +1（最后一段）
#
# 说明：Dalamud SDK 的 DalamudPackager 会自动在
#       src\bin\Release\<AssemblyName>\latest.zip 生成发布包，
#       本脚本负责构建、取出该 zip、并同步 pluginmaster.json 的版本与时间戳。

param(
    [switch]$BumpVersion
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path $PSScriptRoot -Parent
$proj       = Join-Path $repoRoot 'src\MCHPvPRotation.csproj'
$dist       = Join-Path $repoRoot 'dist'
$zipOut     = Join-Path $dist 'latest.zip'
$masterJson = Join-Path $repoRoot 'pluginmaster.json'
$assembly   = 'MCHPvPRotation'

# ---------------------------------------------------------------- 环境
$dotnet = 'C:\Users\Administrator\.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$env:DOTNET_ROOT = 'C:\Users\Administrator\.dotnet'
$env:DALAMUD_HOME = "$env:APPDATA\XIVLauncherCN\addon\Hooks\dev"

if (-not (Test-Path $env:DALAMUD_HOME)) { throw "找不到 Dalamud 目录: $env:DALAMUD_HOME" }

# ---------------------------------------------------------------- 版本号
$csproj = [System.IO.File]::ReadAllText($proj, [System.Text.UTF8Encoding]::new($false))
if ($BumpVersion) {
    $m = [regex]::Match($csproj, '<Version>(\d+)\.(\d+)\.(\d+)\.(\d+)</Version>')
    if (-not $m.Success) { throw "csproj 里找不到 <Version>x.x.x.x</Version>" }
    $new = "$($m.Groups[1].Value).$($m.Groups[2].Value).$($m.Groups[3].Value).$([int]$m.Groups[4].Value + 1)"
    $csproj = $csproj.Replace($m.Value, "<Version>$new</Version>")
    [System.IO.File]::WriteAllText($proj, $csproj, [System.Text.UTF8Encoding]::new($false))
    Write-Host "版本号 -> $new" -ForegroundColor Green
}
$ver = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
Write-Host "版本: $ver" -ForegroundColor Cyan

# ---------------------------------------------------------------- 构建
Write-Host ""
Write-Host "=== 构建 ===" -ForegroundColor Cyan
Remove-Item (Join-Path $repoRoot 'src\bin\Release') -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet build $proj -c Release -t:Rebuild -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "构建失败" }

# ---------------------------------------------------------------- 取出发布包
$sdkZip = Join-Path $repoRoot "src\bin\Release\$assembly\latest.zip"
if (-not (Test-Path $sdkZip)) { throw "SDK 没有生成发布包: $sdkZip" }

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item $sdkZip $zipOut -Force
$zi = Get-Item $zipOut
Write-Host ""
Write-Host ("发布包: {0}  ({1} KB)" -f $zipOut, [math]::Round($zi.Length / 1KB, 1)) -ForegroundColor Green

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipOut)
try {
    Write-Host "包含:"
    foreach ($e in $zip.Entries) { Write-Host ("  {0,-32} {1} 字节" -f $e.Name, $e.Length) }
} finally { $zip.Dispose() }

# ---------------------------------------------------------------- 同步 pluginmaster.json
if (Test-Path $masterJson) {
    $raw = [System.IO.File]::ReadAllText($masterJson, [System.Text.UTF8Encoding]::new($false))
    $raw = [regex]::Replace($raw, '"AssemblyVersion"\s*:\s*"[^"]*"', "`"AssemblyVersion`": `"$ver`"")
    $stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $raw = [regex]::Replace($raw, '"LastUpdate"\s*:\s*[^,\r\n]+', "`"LastUpdate`": $stamp")
    [System.IO.File]::WriteAllText($masterJson, $raw, [System.Text.UTF8Encoding]::new($false))
    Write-Host ""
    Write-Host "已更新 pluginmaster.json: AssemblyVersion=$ver LastUpdate=$stamp" -ForegroundColor Green
}

Write-Host ""
Write-Host "下一步:" -ForegroundColor Yellow
Write-Host "  1) 上传 dist\latest.zip 到 GitHub Release（tag = $ver）"
Write-Host "  2) 提交并推送 pluginmaster.json"
