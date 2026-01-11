param(
        [Parameter(Mandatory)]
        [string]
    $PogPath,
        [Parameter(Mandatory)]
        [string]
    $Version
)

Set-StrictMode -Version 3
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$Tag = if ($Version -eq "nightly") {
    "nightly"
} else {
    "v$([version]$Version)"
}

$ArchivePath = "${PogPath}.zip"
$ReleaseUrl = "https://github.com/MatejKafka/Pog/releases/download/$Tag/Pog-$Tag.zip"

Write-Host "Installing Pog $Tag from '$ReleaseUrl'..."

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
