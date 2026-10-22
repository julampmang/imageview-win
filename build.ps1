#requires -version 5.1
<#
    ImageView 构建脚本
    使用系统自带的 C# 编译器(csc.exe)，不需要安装任何 SDK。
    用法:
      .\build.ps1               构建到 dist\ImageView.exe
      .\build.ps1 -Clean        先清理 dist
#>
[CmdletBinding()]
param(
    [string]$OutDir = 'dist',
    [switch]$Clean,
    # 可选：给产物做 Authenticode 签名（消除“未知发布者”提示）
    [string]$PfxPath,
    [securestring]$PfxPassword,
    [string]$CertThumbprint,
    [switch]$NoTimestamp
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "找不到系统自带的 C# 编译器: $csc"
}

$distDir = Join-Path $root $OutDir
if ($Clean -and (Test-Path -LiteralPath $distDir)) {
    Remove-Item -Recurse -Force -LiteralPath $distDir
}
if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
}

# ---- 图标（缺失时自动生成） ----
$iconPath = Join-Path $root 'assets\app.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    $mk = Join-Path $root 'tools\make-icon.py'
    $python = 'C:\Users\Administrator\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
    if (-not (Test-Path -LiteralPath $python)) { $python = 'python' }
    if (Test-Path -LiteralPath $mk) {
        New-Item -ItemType Directory -Force -Path (Join-Path $root 'assets') | Out-Null
        & $python $mk
        if ($LASTEXITCODE -ne 0) { Write-Warning '图标生成失败，将使用默认图标构建' }
    }
}

# ---- 源文件 ----
$sources = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -Filter *.cs |
    Sort-Object FullName | Select-Object -ExpandProperty FullName
if (-not $sources) { throw 'src 目录下没有 .cs 文件' }

$exePath = Join-Path $distDir 'ImageView.exe'

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:x64'
    '/optimize+'
    '/warn:4'
    # 源码用 UTF-8 保存，强制按 UTF-8 读取，避免中文文案变乱码
    '/codepage:65001'
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
    ('/win32manifest:' + (Join-Path $root 'app.manifest'))
    ('/out:' + $exePath)
)
if (Test-Path -LiteralPath $iconPath) {
    $cscArgs += ('/win32icon:' + $iconPath)
}
$cscArgs += $sources

# 正在运行的旧版本会锁住 exe，先关掉（只针对输出目录里的这一个文件）
try {
    $running = Get-Process -Name 'ImageView' -ErrorAction SilentlyContinue
    foreach ($proc in $running) {
        $procPath = $null
        try { $procPath = $proc.Path } catch { }
        if ($procPath -and ($procPath -eq $exePath)) {
            Write-Host "  检测到正在运行的 ImageView，先关闭以完成替换" -ForegroundColor Yellow
            $proc.CloseMainWindow() | Out-Null
            if (-not $proc.WaitForExit(1500)) { $proc.Kill(); $proc.WaitForExit(1500) | Out-Null }
        }
    }
} catch { }

$sw = [System.Diagnostics.Stopwatch]::StartNew()
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    & chcp.com 65001 | Out-Null   # 让 csc 的中文报错能正常显示
} catch { }
$ErrorActionPreference = 'Continue'
$compilerOutput = & $csc @cscArgs 2>&1 | ForEach-Object { $_.ToString() }
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$sw.Stop()

foreach ($line in $compilerOutput) {
    if ($line -match 'error ') { Write-Host $line -ForegroundColor Red }
    elseif ($line -match 'warning ') { Write-Host $line -ForegroundColor Yellow }
    elseif ($line.Trim()) { Write-Host $line }
}

if ($code -ne 0) {
    throw "编译失败 (csc exit $code)"
}

# ---- 去掉“来自 Internet”标记 ----
# 如果工程目录本身是从网络位置/压缩包解出来的，产物可能继承 Zone.Identifier，
# 那样第一次双击会弹 SmartScreen「无法识别的应用」。这里直接清掉，保证产物是“本地文件”。
try {
    Unblock-File -LiteralPath $exePath -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $exePath -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue
} catch { }

# ---- 可选：Authenticode 签名 ----
$signCert = $null
try {
    if ($PfxPath) {
        if (-not (Test-Path -LiteralPath $PfxPath)) { throw "找不到证书文件: $PfxPath" }
        if ($PfxPassword) {
            $signCert = Get-PfxCertificate -FilePath $PfxPath -Password $PfxPassword
        } else {
            $signCert = Get-PfxCertificate -FilePath $PfxPath
        }
    } elseif ($CertThumbprint) {
        $signCert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $CertThumbprint } | Select-Object -First 1
        if (-not $signCert) { throw "证书库 CurrentUser\My 里找不到指纹 $CertThumbprint 的代码签名证书" }
    }

    if ($signCert) {
        Write-Host ("签名证书: {0}" -f $signCert.Subject)
        if ($NoTimestamp) {
            $sig = Set-AuthenticodeSignature -LiteralPath $exePath -Certificate $signCert
        } else {
            $sig = Set-AuthenticodeSignature -LiteralPath $exePath -Certificate $signCert `
                -TimestampServer 'http://timestamp.digicert.com'
            if ($sig.Status -ne 'Valid') {
                Write-Warning ("带时间戳签名失败({0})，改用无时间戳重试" -f $sig.Status)
                $sig = Set-AuthenticodeSignature -LiteralPath $exePath -Certificate $signCert
            }
        }
        Write-Host ("签名状态: {0}" -f $sig.Status) -ForegroundColor $(if ($sig.Status -eq 'Valid') { 'Green' } else { 'Yellow' })
        if ($sig.Status -ne 'Valid' -and $sig.StatusMessage) {
            Write-Warning ("签名提示: {0}" -f $sig.StatusMessage)
        }
        try {
            Unblock-File -LiteralPath $exePath -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $exePath -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue
        } catch { }
    }
} catch {
    Write-Warning ("签名环节跳过: {0}" -f $_.Exception.Message)
}

$item = Get-Item -LiteralPath $exePath
Write-Host ("构建成功: {0}" -f $item.FullName) -ForegroundColor Green
Write-Host ("  体积   : {0:N0} KB" -f ($item.Length / 1KB))
Write-Host ("  文件数 : {0}" -f $sources.Count)
Write-Host ("  耗时   : {0} ms" -f $sw.ElapsedMilliseconds)
if (-not $signCert) {
    Write-Host "  签名   : 未签名（本机可正常打开；分发给别人时建议用 -PfxPath 或 -CertThumbprint 签名）" -ForegroundColor DarkGray
}
Write-Host ""
Write-Host "提示: 把 exe 复制到别处后如果弹出 SmartScreen，执行  Unblock-File .\ImageView.exe  即可解除。" -ForegroundColor DarkGray
exit 0
