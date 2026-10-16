param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/Build.ps1" -Configuration $Configuration
$naviliteExecutable = Join-Path $PSScriptRoot "../src/NaviLite.Desktop/bin/$Configuration/net8.0-windows/NaviLite.Desktop.exe"
& $naviliteExecutable
