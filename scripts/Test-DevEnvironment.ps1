$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Enter-Dev.ps1"
$failed = $false
foreach ($tool in @('git', 'dotnet', 'rustc', 'cargo')) {
    if (Get-Command $tool -ErrorAction SilentlyContinue) {
        & $tool --version
        if ($LASTEXITCODE -ne 0) { $failed = $true }
    } else {
        Write-Host "MISSING: $tool"
        $failed = $true
    }
}
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $sdks = & dotnet --list-sdks
    if (-not ($sdks -match '^8\.0\.')) {
        Write-Host 'MISSING: .NET SDK 8.0'
        $failed = $true
    }
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (Test-Path $vswhere) {
    $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vs) { Write-Host "MSVC: $vs" }
    else { Write-Host 'MISSING: MSVC C++ build tools'; $failed = $true }
} else {
    Write-Host 'MISSING: Visual Studio Build Tools'
    $failed = $true
}
$windowsSdkLib = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/Lib'
$kernelLib = Get-ChildItem "$windowsSdkLib/*/um/x64/kernel32.lib" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($kernelLib) { Write-Host "Windows SDK: $($kernelLib.Directory.Parent.Parent.Name)" }
else { Write-Host 'MISSING: Windows SDK x64 libraries'; $failed = $true }
$tcp = [System.Net.Sockets.TcpClient]::new()
try {
    $connection = $tcp.ConnectAsync('127.0.0.1', 3306)
    if (-not $connection.Wait(2000)) { throw 'timeout' }
    Write-Host 'MySQL: TCP 127.0.0.1:3306 reachable (authentication not checked)'
} catch {
    Write-Host 'MySQL: optional local server is not reachable at 127.0.0.1:3306'
} finally { $tcp.Dispose() }
if ($failed) { throw 'Development prerequisites are incomplete.' }
Write-Host 'Development toolchain checks passed.'
