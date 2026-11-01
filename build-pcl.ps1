<#
    PCL-main 构建脚本

    用法（本机执行策略为 Restricted，必须显式 Bypass）：
        powershell -ExecutionPolicy Bypass -File build-pcl.ps1
        powershell -ExecutionPolicy Bypass -File build-pcl.ps1 -Configuration Release
        powershell -ExecutionPolicy Bypass -File build-pcl.ps1 -Rebuild

    背景：
      1. 本机 .NET SDK 为节省 C 盘空间装在 D:\dotnet，且未向 VS BuildTools 注册组件，
         因此 VS 版 MSBuild.exe 无法自动解析 SDK 风格项目（PCLCS / MeloongCore）。
         本脚本用 MSBuildSDKsPath + MSBuildEnableWorkloadResolver=false 显式接管解析。
      2. .NET Framework 4.8 引用程序集位于 C:\Program Files (x86)\Reference Assemblies。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release', 'Snapshot')]
    [string]$Configuration = 'Debug',

    [switch]$Rebuild,

    [string]$Solution = 'D:\代码\pcl\PCL-main\Plain Craft Launcher 2.sln'
)

$ErrorActionPreference = 'Stop'

$DotnetRoot   = 'C:\Program Files\dotnet'
$VsBuildTools = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools'

# --- 定位 MSBuild ---
$msbuild = Join-Path $VsBuildTools 'MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path $msbuild)) { throw "未找到 MSBuild: $msbuild" }

# --- 定位 .NET SDK 并接管 SDK 解析 ---
$sdkDir = Get-ChildItem (Join-Path $DotnetRoot 'sdk') -Directory -ErrorAction SilentlyContinue |
          Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdkDir) { throw "未在 $DotnetRoot\sdk 找到任何 SDK" }

$env:MSBuildSDKsPath              = Join-Path $sdkDir.FullName 'Sdks'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_ROOT                  = $DotnetRoot
$env:DOTNET_HOST_PATH             = Join-Path $DotnetRoot 'dotnet.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT  = '1'

# --- 日志 ---
$logDir = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$log = Join-Path $logDir ("build-{0}-{1}.log" -f $Configuration, (Get-Date -Format 'yyyyMMdd-HHmmss'))

$target = if ($Rebuild) { 'Rebuild' } else { 'Build' }
Write-Host ("配置={0}  目标={1}  SDK={2}" -f $Configuration, $target, $sdkDir.Name) -ForegroundColor Cyan

# 构建阶段用 Continue，避免原生命令 stderr 被 ErrorActionPreference=Stop 当作终止错误
$ErrorActionPreference = 'Continue'
& $msbuild $Solution /t:$target /p:Configuration=$Configuration /m:1 /v:m /nologo 2>&1 |
    Tee-Object -FilePath $log |
    ForEach-Object { Write-Host $_ }

$code = $LASTEXITCODE
if ($code -eq 0) {
    $exe = 'D:\代码\pcl\PCL-main\Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe'
    if (Test-Path $exe) { Write-Host ("产物: {0:N0} KB  版本 {1}" -f ((Get-Item $exe).Length/1KB), (Get-Item $exe).VersionInfo.FileVersion) }
}
Write-Host ''
Write-Host ("退出码={0}  日志={1}" -f $code, $log) -ForegroundColor $(if ($code -eq 0) { 'Green' } else { 'Red' })
exit $code
