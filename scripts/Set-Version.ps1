param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$naviliteVersionPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$'
if ($Version -cnotmatch $naviliteVersionPattern) { throw 'Use a valid Semantic Version, for example 0.2.0 or 0.2.0-beta.1.' }
$naviliteAssemblyVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
foreach ($part in $naviliteAssemblyVersion.Split('.')) {
    if ([long]$part -gt 65534) { throw '.NET assembly version components must be between 0 and 65534.' }
}
$naviliteRoot = Split-Path $PSScriptRoot -Parent
$naviliteCargoPath = Join-Path $naviliteRoot 'Cargo.toml'
$navilitePropsPath = Join-Path $naviliteRoot 'Directory.Build.props'
$naviliteLockPath = Join-Path $naviliteRoot 'Cargo.lock'
$naviliteOriginalCargo = [IO.File]::ReadAllText($naviliteCargoPath)
$naviliteOriginalProps = [IO.File]::ReadAllText($navilitePropsPath)
$naviliteOriginalLock = [IO.File]::ReadAllText($naviliteLockPath)
$naviliteUpdatedCargoText = [regex]::Replace($naviliteOriginalCargo, '(?m)^version = "[^"]+"$', "version = `"$Version`"")
$naviliteProps = [regex]::Replace($naviliteOriginalProps, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
$naviliteProps = [regex]::Replace($naviliteProps, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$naviliteAssemblyVersion</AssemblyVersion>")
$naviliteProps = [regex]::Replace($naviliteProps, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$naviliteAssemblyVersion</FileVersion>")
. "$PSScriptRoot/Enter-Dev.ps1"
Push-Location $naviliteRoot
try {
    [IO.File]::WriteAllText($naviliteCargoPath, $naviliteUpdatedCargoText)
    [IO.File]::WriteAllText($navilitePropsPath, $naviliteProps)
    & cargo update --package navilite-core --offline
    if ($LASTEXITCODE -ne 0) { throw 'Cannot update Cargo.lock.' }
    Write-Host "NaviLite version updated to $Version. Update CHANGELOG.md before committing and tagging v$Version."
} catch {
    [IO.File]::WriteAllText($naviliteCargoPath, $naviliteOriginalCargo)
    [IO.File]::WriteAllText($navilitePropsPath, $naviliteOriginalProps)
    [IO.File]::WriteAllText($naviliteLockPath, $naviliteOriginalLock)
    throw
} finally { Pop-Location }
