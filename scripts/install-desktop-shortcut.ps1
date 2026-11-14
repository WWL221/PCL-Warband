# 构建 → 复制到固定目录 → 在桌面建快捷方式
#
# 用法（在仓库根目录执行）：
#   powershell -ExecutionPolicy Bypass -File .\scripts\install-desktop-shortcut.ps1
#   powershell -ExecutionPolicy Bypass -File .\scripts\install-desktop-shortcut.ps1 -TargetDir "D:\game\PCL-Warband"
#
# 说明：PCL 会把自身的 PCL\Setup.ini、日志等写在 exe 同级目录，
# 所以这里刻意复制到固定目录再建快捷方式，避免直接指向 bin\Release 被重新构建冲掉。

param(
    [string]$TargetDir = (Join-Path $env:LOCALAPPDATA 'PCL-Warband'),
    [string]$ShortcutName = 'PCL 战团版',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj'
$output = Join-Path $repoRoot 'Plain Craft Launcher 2\bin\Release\net48'
$exeName = 'Plain Craft Launcher 2.exe'

if (-not $SkipBuild) {
    Write-Host "[1/3] 构建 Release ..." -ForegroundColor Cyan
    dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw "构建失败（exit $LASTEXITCODE）" }
}

$builtExe = Join-Path $output $exeName
if (-not (Test-Path $builtExe)) { throw "找不到构建产物：$builtExe（先去掉 -SkipBuild 跑一次构建）" }

Write-Host "[2/3] 复制到 $TargetDir ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
Copy-Item -Path (Join-Path $output '*') -Destination $TargetDir -Recurse -Force

$targetExe = Join-Path $TargetDir $exeName
if (-not (Test-Path $targetExe)) { throw "复制后仍找不到 $targetExe" }

Write-Host "[3/3] 创建桌面快捷方式 ..." -ForegroundColor Cyan
$desktop = [Environment]::GetFolderPath('Desktop')
$lnkPath = Join-Path $desktop "$ShortcutName.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnkPath)
$shortcut.TargetPath = $targetExe
$shortcut.WorkingDirectory = $TargetDir
$shortcut.IconLocation = "$targetExe,0"
$shortcut.Description = 'PCL 战团版 —— 骑砍战团 / WSE2 启动器'
$shortcut.WindowStyle = 1
$shortcut.Save()

Write-Host ""
Write-Host "完成：" -ForegroundColor Green
Write-Host "  程序目录   $TargetDir"
Write-Host "  快捷方式   $lnkPath"
Write-Host "  配置文件   (首次运行后生成) $TargetDir\PCL\Setup.ini"
