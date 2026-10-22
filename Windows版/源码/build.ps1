param(
    [switch]$SkipSingle        # 只编 XP~Win11 版，不编单文件免配置版
)
# 编译「APK 提取工具」（Windows 版）—— 用系统自带的 csc，零第三方依赖
#
#   产物 1：build\APK提取工具.exe                    —— 用 CLR 2.0 的 csc 编 + exe.config 同时声明
#                                                      v4.0/v2.0.50727 → XP ~ Win11 一份 exe 通吃
#   产物 2：build\APK提取工具(单文件免配置版).exe      —— 用 CLR 4.0 的 csc 编，不需要配置文件
#                                                      （系统有 .NET 4 就行，Win8+ 默认都有）
#
# 两个坑：
#   ① **中文源码必须 /codepage:65001**（src\*.cs 是 UTF-8 无 BOM，不指定 csc 会按 GBK 读，中文全乱）；
#   ② 加了新 .cs 文件不用改这个脚本 —— 源码是通配收的（src\*.cs + src\Lzma\*.cs）。
$ErrorActionPreference = 'Continue'

$proj  = $PSScriptRoot
$srcDir = Join-Path $proj 'src'
$outDir = Join-Path $proj 'build'
$icon   = Join-Path $proj 'assets\app.ico'
$csc2   = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v2.0.50727\csc.exe'
$csc4   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc4)) { $csc4 = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

if (-not (Test-Path $srcDir)) { throw "找不到源码目录：$srcDir" }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$files = @()
$files += (Get-ChildItem $srcDir -Filter *.cs | ForEach-Object { $_.FullName })
$files += (Get-ChildItem (Join-Path $srcDir 'Lzma') -Filter *.cs | ForEach-Object { $_.FullName })
Write-Output ("源文件 " + $files.Count + " 个")

function Invoke-Csc([string]$csc, [string]$out) {
    if (-not (Test-Path $csc)) { Write-Output ("  跳过（没有 " + $csc + "）"); return $false }
    if (Test-Path $out) { Remove-Item $out -Force }
    $args = @('/nologo', '/target:winexe', '/codepage:65001', '/optimize+',
              ('/win32icon:' + $icon), ('/out:' + $out)) + $files
    & $csc @args
    if ($LASTEXITCODE -ne 0) { throw ("csc 失败：" + $csc) }
    return $true
}

Write-Output '--- 1/2 XP ~ Win11 版（CLR 2.0 编译）---'
$exe = Join-Path $outDir 'APK提取工具.exe'
if (Invoke-Csc $csc2 $exe) {
    $cfg = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup>
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.0"/>
    <supportedRuntime version="v2.0.50727"/>
  </startup>
</configuration>
'@
    # 配置文件也必须是 UTF-8（无 BOM 即可，.NET 认 XML 声明的 utf-8）
    [System.IO.File]::WriteAllText((Join-Path $outDir 'APK提取工具.exe.config'), ($cfg -replace "`r?`n", "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
    Write-Output ("  " + (Split-Path $exe -Leaf) + " = " + [math]::Round((Get-Item $exe).Length / 1KB, 1) + " KB (+ 同名 .exe.config)")
}

if (-not $SkipSingle) {
    Write-Output '--- 2/2 单文件免配置版（CLR 4.0 编译）---'
    $exe2 = Join-Path $outDir 'APK提取工具(单文件免配置版).exe'
    if (Invoke-Csc $csc4 $exe2) {
        Write-Output ("  " + (Split-Path $exe2 -Leaf) + " = " + [math]::Round((Get-Item $exe2).Length / 1KB, 1) + " KB")
    }
}

Write-Output '编译完成。'
