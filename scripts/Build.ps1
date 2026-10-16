param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Enter-Dev.ps1"
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $cargoArgs = @('build', '--workspace', '--locked')
    if ($Configuration -eq 'Release') { $cargoArgs += '--release' }
    & cargo @cargoArgs
    if ($LASTEXITCODE -ne 0) { throw 'Rust build failed.' }
    & dotnet build NaviLite.sln --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'WPF build failed.' }
} finally { Pop-Location }
