# Dot-source this file: . ./scripts/Enter-Dev.ps1
$naviliteDotnet = Join-Path $env:LOCALAPPDATA 'NaviLite/dotnet'
$naviliteCargo = Join-Path $env:USERPROFILE '.cargo/bin'
if (Test-Path (Join-Path $naviliteDotnet 'dotnet.exe')) {
    $env:DOTNET_ROOT = $naviliteDotnet
    if (($env:PATH -split ';') -notcontains $naviliteDotnet) {
        $env:PATH = "$naviliteDotnet;$env:PATH"
    }
}
if (Test-Path $naviliteCargo) {
    if (($env:PATH -split ';') -notcontains $naviliteCargo) {
        $env:PATH = "$naviliteCargo;$env:PATH"
    }
}
$naviliteVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if ((Test-Path $naviliteVswhere) -and $env:VSCMD_ARG_TGT_ARCH -ne 'x64') {
    $naviliteVs = & $naviliteVswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($naviliteVs) {
        $naviliteDevShell = Join-Path $naviliteVs 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll'
        if (Test-Path $naviliteDevShell) {
            Import-Module $naviliteDevShell
            Enter-VsDevShell -VsInstallPath $naviliteVs -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
        }
    }
}
