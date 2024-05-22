param(
    ### Do not add Pog to PATH and PSModulePath. You must then import Pog into your
    ### PowerShell session manually using `Import-Module` with a path to the module,
    ### and invoke exported commands using absolute path.
    [switch]$NoEnv,

    ### Do not automatically update user-wide PowerShell execution policy to allow running local scripts.
    [switch]$NoExecutionPolicy
)

Set-StrictMode -Version Latest

$ErrorActionPreference = "Stop"
$PSDefaultParameterValues = @{
    "*:ErrorAction" = "Stop"
}


if ($PSVersionTable.PSVersion -lt "5.0") {
    throw "Pog requires at least PowerShell 5."
}
if ($env:PROCESSOR_ARCHITECTURE -ne "AMD64") {
    throw "Pog requires x64. Sorry, ARM is not supported for now."
}


$SymlinkPath = "$env:TEMP\Pog-symlink-test-$(New-Guid)"
try {
    # try creating a symlink in TEMP to itself
    $null = cmd.exe /c mklink $SymlinkPath $SymlinkPath
    $SymlinksAllowed = $LASTEXITCODE -eq 0
} catch {
    $SymlinksAllowed = $false
}
Remove-Item $SymlinkPath -ErrorAction Ignore

if (-not $SymlinksAllowed) {
    throw ("Pog cannot create symbolic links, which are currently necessary for correct functionality.`n" +`
        "To allow creating symbolic links, do any of the following and re-run '$PSCommandPath':`n" +`
        "  1) Enable Developer Mode. (Settings -> Update & Security -> For developers -> Developer Mode)`n" +`
        "  2) Allow your user account to create symbolic links using Group Policy.`n" +`
        "     (Windows Settings -> Security Settings -> Local Policies -> User Rights Assignment -> Create symbolic links)`n" +`
        "  3) Always run Pog as administrator.")
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
    # use a relative path, so that the package root is valid even if Pog is moved
    Set-Content $ROOT_FILE_PATH -Value "..\.."
}

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
        Write-Information ""
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
        Write-Information ""
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

if ($NoEnv) {
    Write-Host "No changes to environment variables were made.`nTo use Pog in a new PowerShell session, run:"
    Write-Host ('    Import-Module "' + $PSScriptRoot + '\app\Pog"') -ForegroundColor Green

    Write-Host "To invoke commands exported by Pog packages, add the 'package_bin' directory to your shell PATH:"
    Write-Host ('    $env:PATH = "' + $PSScriptRoot + '\data\package_bin;$env:PATH"') -ForegroundColor Green
} else {
    Write-Host "Setting up PATH and PSModulePath..."
    # add Pog dir to PSModulePath
    Add-EnvPSModulePath -Prepend (Resolve-Path "$PSScriptRoot\app")
    # add binary dir to PATH
    Add-EnvPath -Prepend (Resolve-Path "$PSScriptRoot\data\package_bin")
}

if ((Get-ExecutionPolicy -Scope CurrentUser) -notin @("RemoteSigned", "Unrestricted", "Bypass")) {
    if ($NoExecutionPolicy) {
        Write-Host "No changes to execution policy were made. Pog likely won't work until you change the execution policy to at least 'RemoteSigned'."
    } else {
        # since Pog is currently not signed, we need at least RemoteSigned to run
        Write-Warning "Changing PowerShell execution policy for the current user to 'RemoteSigned'..."
        # https://stackoverflow.com/questions/60541618/how-to-suppress-warning-message-from-script-when-calling-set-executionpolicy/60549569#60549569
        try {Set-ExecutionPolicy -Scope CurrentUser RemoteSigned -Force} catch [System.Security.SecurityException] {}
    }
}


Write-Host ""
Write-Host "It seems Pog is now correctly set up. :)"
Write-Host ""
Write-Host "To install a package, run the following command (press Ctrl+Space for auto-complete):"
Write-Host '    pog <PackageName>' -ForegroundColor Green
Write-Host "To access the documentation, use the built-in PowerShell help system:"
Write-Host '    man about_Pog' -ForegroundColor Green
