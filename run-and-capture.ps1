#requires -version 5.1
<#
    启动 ImageView 并截图，用于人工/自动检查界面。
    用法:
      .\run-and-capture.ps1                       空窗口
      .\run-and-capture.ps1 -File 路径\图片.jpg    打开指定图片
      .\run-and-capture.ps1 -Name dark            指定输出文件名
#>
[CmdletBinding()]
param(
    [string]$File,
    [string]$Name = 'window',
    [int]$WaitMs = 900,
    [string]$OutDir = 'docs\screenshots',
    [string]$Theme,
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'dist\ImageView.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "先运行 build.ps1：找不到 $exe" }

$capExe = Join-Path $root 'tools\capture.exe'
if (-not (Test-Path -LiteralPath $capExe)) {
    & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 `
        /r:System.Drawing.dll ('/out:' + $capExe) (Join-Path $root 'tools\capture.cs') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'capture.exe 编译失败' }
}

$outDirAbs = Join-Path $root $OutDir
if (-not (Test-Path -LiteralPath $outDirAbs)) { New-Item -ItemType Directory -Force -Path $outDirAbs | Out-Null }

$appArgs = @()
if ($File) { $appArgs += $File }
if ($Theme) { $appArgs += ("--theme=" + $Theme) }

# 重定向子进程 stdio：否则 GUI 进程会一直占着父进程的输出管道，导致脚本卡住
$stdoutFile = Join-Path $env:TEMP 'imageview-run.out'
$stderrFile = Join-Path $env:TEMP 'imageview-run.err'
$proc = Start-Process -FilePath $exe -ArgumentList $appArgs -PassThru `
    -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
try {
    $deadline = (Get-Date).AddMilliseconds($WaitMs + 3000)
    $hwnd = [IntPtr]::Zero
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 120
        $p = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        if (-not $p) { throw '进程已退出' }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $p.MainWindowHandle; break }
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw '等不到主窗口句柄' }

    Start-Sleep -Milliseconds $WaitMs
    $out = Join-Path $outDirAbs ($Name + '.png')
    & $capExe $hwnd.ToInt64() $out
    if ($LASTEXITCODE -ne 0) { throw '截图失败' }
    Write-Host ("截图: {0}" -f $out) -ForegroundColor Green
}
finally {
    if (-not $Keep) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
}
