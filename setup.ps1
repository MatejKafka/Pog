Set-StrictMode -Version Latest

$ErrorActionPreference = "Stop"
$PSDefaultParameterValues = @{
    "*:ErrorAction" = "Stop"
}


if ($PSVersionTable.PSVersion -lt "5.0") {
    throw "Pog requires at least PowerShell 5."
}

# need to call unblock before importing any modules
Write-Host "Unblocking Pog PowerShell files..."
& {
    gi $PSScriptRoot/app/Pog/lib_compiled/*
    ls -Recurse $PSScriptRoot/app -File -Filter *.ps*1
 } | Unblock-File

# these modules only use library functions, so it is safe to import them even during setup
Import-Module $PSScriptRoot/app/Pog/container/container_lib/Environment
Import-Module $PSScriptRoot/app/Pog/lib/Utils


function newdir($Dir) {
    $Dir = Resolve-VirtualPath $PSScriptRoot/$Dir
    if (Test-Path -PathType Container $Dir) {
        return
    }
    if (Test-Path $Dir) {
        # exists, but not a directory
        $null = rm -Recurse $Dir
    }
    Write-Host "Creating directory '$Dir'."
    $null = New-Item -Type Directory -Force $Dir
}

Write-Host "Setting up all directories required by Pog..."

newdir "./data"
newdir "./cache"
# local manifest repository
# ./data/manifests is created below
newdir "./data/manifest_generators"
# directory where commands are exported; is added to PATH
newdir "./data/package_bin"
# downloaded package cache
newdir "./cache/download_cache"
newdir "./cache/download_tmp"

$ROOT_FILE_PATH = Join-Path $PSScriptRoot "./data/package_roots.txt"
if (-not (Test-Path -PathType Leaf $ROOT_FILE_PATH)) {
    $DefaultContentRoot = Resolve-Path $PSScriptRoot\..
    Write-Host "Registering a Pog package root at '$DefaultContentRoot'..."
    Set-Content $ROOT_FILE_PATH -Value $DefaultContentRoot
}

# ====================================================================================
# now, we should be ready to import Pog
Import-Module $PSScriptRoot\app\Pog

try {
    $7z, $ofv = Get-PogPackage 7zip, OpenedFilesView
} catch {
    throw "Could not find the packages '7zip' and 'OpenedFilesView', required for correct functioning of Pog. " +`
        "Both packages should be provided with Pog itself. Please install Pog from a release, not by cloning the repository directly."
    return
}

try {
    $7z | Enable-Pog -PassThru | Export-Pog
} catch {
    throw ("Failed to enable the '7zip' package, required for correct functioning of Pog: " + $_)
    return
}

try {
    $ofv | Enable-Pog -PassThru | Export-Pog
} catch {
    throw ("Failed to enable the 'OpenedFilesView' package, required for correct functioning of Pog: " + $_)
    return
}

if (-not (Test-Path "$PSScriptRoot\data\package_bin\7z.exe")) {
    throw "Setup of '7zip' was successful, but we cannot find the 7z.exe binary that should be provided by the package."
    return
}

if (-not (Test-Path "$PSScriptRoot\data\package_bin\OpenedFilesView.exe")) {
    throw "Setup of 'OpenedFilesView' was successful, but we cannot find the OpenedFilesView.exe binary that should be provided by the package."
    return
}

Write-Host ""


# we download the package repository after enabling 7zip to be able to use it for extraction
# TODO: when a more comprehensive support for remote repository is implemented, update this
$MANIFEST_REPO_PATH = Resolve-VirtualPath "$PSScriptRoot/data/manifests"
if (-not (Test-Path $MANIFEST_REPO_PATH)) {
    # manifest repository is not initialized, download it
    Write-Host "Downloading package manifests from 'https://github.com/MatejKafka/PogPackages'..."
    if (Get-Command git -ErrorAction Ignore) {
        Write-Host "Cloning using git..."
        git clone https://github.com/MatejKafka/PogPackages $MANIFEST_REPO_PATH --depth 1 --quiet
    } else {
        $TmpDir = "${env:TEMP}\PogPackages-$(New-Guid)"
        $ExtractedPath = "$TmpDir\PogPackages"
        try {
            # use internal functions for download and extraction
            Import-Module $PSScriptRoot\app\Pog\lib_compiled\Pog.dll

            $ArchivePath = Invoke-FileDownload "https://github.com/MatejKafka/PogPackages/archive/refs/heads/main.zip" $TmpDir
            # Expand-Archive in powershell.exe is incredibly slow for small files, so we use 7zip
            Expand-Archive7Zip $ArchivePath $ExtractedPath
            Move-Item $ExtractedPath\* $MANIFEST_REPO_PATH -Force
        } finally {
            Remove-Item $TmpDir -Recurse -Force -ErrorAction Ignore
        }
    }
    Write-Host "Downloaded '$(@(ls -Directory $MANIFEST_REPO_PATH).Count)' package manifests."
}


# TODO: prompt before doing these user-wide changes

Write-Host "Setting up PATH and PSModulePath..."
# add Pog dir to PSModulePath
Add-EnvPSModulePath (Resolve-Path "$PSScriptRoot\app")
# add binary dir to PATH
Add-EnvPath -Prepend (Resolve-Path "$PSScriptRoot\data\package_bin")

if ((Get-ExecutionPolicy -Scope CurrentUser) -notin @("RemoteSigned", "Unrestricted", "Bypass")) {
    # since Pog is currently not signed, we need at least RemoteSigned to run
    Write-Warning "Changing PowerShell execution policy for the current user to 'RemoteSigned'..."
    # https://stackoverflow.com/questions/60541618/how-to-suppress-warning-message-from-script-when-calling-set-executionpolicy/60549569#60549569
    try {Set-ExecutionPolicy -Scope CurrentUser RemoteSigned -Force} catch [System.Security.SecurityException] {}
}


Write-Host ""

# now, everything should be setup correctly, enable Pog itself to validate (it doesn't do anything, just prints a success message)
Enable-Pog Pog

Write-Host ""