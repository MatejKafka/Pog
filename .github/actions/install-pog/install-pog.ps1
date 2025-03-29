param(
        [Parameter(Mandatory)]
        [string]
    $PogPath,
        [Parameter(Mandatory)]
        [version]
    $Version
)

Set-StrictMode -Version 3
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$ArchivePath = "${PogPath}.zip"
$ReleaseUrl = "https://github.com/MatejKafka/Pog/releases/download/v$Version/Pog-v$Version.zip"

Write-Host "Installing Pog v$Version from '$ReleaseUrl'..."

# download Pog
iwr $ReleaseUrl -OutFile $ArchivePath

# unpack Pog, tar is much faster than Expand-Archive
$null = mkdir $PogPath
tar -xf $ArchivePath --directory $PogPath

# setup Pog
& $PogPath\Pog\setup.ps1 -Enable None

# propagate PATH and PSModulePath for the following steps
Add-Content $env:GITHUB_PATH (Resolve-Path $PogPath\Pog\data\package_bin)
Add-Content $env:GITHUB_ENV "PSModulePath=$env:PSModulePath"
