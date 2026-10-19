param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Enter-Dev.ps1"
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $cargoArgs = @('build', '--workspace', '--locked')
    if ($Configuration -eq 'Release') { $cargoArgs += '--release' }
    & cargo @cargoArgs
    if ($LASTEXITCODE -ne 0) { throw 'Rust build failed.' }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        & dotnet build NaviLite.sln --configuration $Configuration
    } else {
        # A separate output keeps the currently running app's executable and DLLs intact.
        & dotnet build src/NaviLite.Desktop/NaviLite.Desktop.csproj --configuration $Configuration --output ([IO.Path]::GetFullPath($OutputDirectory))
    }
    if ($LASTEXITCODE -ne 0) { throw 'WPF build failed.' }
} finally { Pop-Location }
