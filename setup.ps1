Set-StrictMode -Version Latest

$ErrorActionPreference = "Stop"
$PSDefaultParameterValues = @{
    "*:ErrorAction" = "Stop"
}


if ($PSVersionTable.PSVersion -lt "5.0") {
    throw "Pog requires at least PowerShell 5."
}

$IsDevModeEnabled = try {
    [bool](Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" "AllowDevelopmentWithoutDevLicense")
} catch {$false}

if (-not $IsDevModeEnabled) {
    throw "Windows Developer mode is not enabled. Currently, it must be enabled for Pog to work correctly, " +`
        "since Pog internally uses symbolic links. Please enable developer mode in Settings."
    return
}

# need to call unblock before importing any modules
Write-Host "Unblocking Pog PowerShell files..."
& {
    gi $PSScriptRoot/app/Pog/lib_compiled/*
    ls -Recurse $PSScriptRoot/app -File -Filter *.ps*1
 } | Unblock-File

# these modules only use library functions, so it is safe to import them even during setup
Import-Module $PSScriptRoot/app/Pog/lib/Environment
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

Write-Host ""

# ====================================================================================
# now, we should be ready to import Pog
Import-Module $PSScriptRoot\app\Pog

if (-not (Test-Path ([Pog.InternalState]::PathConfig.Path7Zip))) {
    try {
        $7z = Get-PogPackage 7zip
    } catch {
        throw "Could not find the '7zip' package, required for correct functioning of Pog. It should be distributed with Pog itself. " +`
            "Please install Pog from a release, not by cloning the repository directly."
        return
    }

    try {
        $7z | Enable-Pog -PassThru | Export-Pog
    } catch {
        throw ("Failed to enable the '7zip' package, required for correct functioning of Pog: " + $_)
        return
    }

    if (-not (Test-Path ([Pog.InternalState]::PathConfig.Path7Zip))) {
        throw "Setup of '7zip' was successful, but we cannot find the 7z.exe binary that should be provided by the package."
        return
    }
}

if (-not (Test-Path ([Pog.InternalState]::PathConfig.PathOpenedFilesView))) {
    try {
        pog OpenedFilesView
    } catch {
        throw ("Failed to install the 'OpenedFilesView' package, required for correct functioning of Pog: " + $_)
        return
    }

    if (-not (Test-Path ([Pog.InternalState]::PathConfig.PathOpenedFilesView))) {
        throw "Setup of 'OpenedFilesView' was successful, but we cannot find the OpenedFilesView.exe binary that should be provided by the package."
        return
    }
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
Write-Host "It seems Pog is setup correctly and working now. :)"
Write-Host ""