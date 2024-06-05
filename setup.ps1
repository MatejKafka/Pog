[CmdletBinding()]
param(
    ### Do not add Pog to PATH and PSModulePath. You must then import Pog into your
    ### PowerShell session manually using `Import-Module` with a path to the module,
    ### and invoke exported commands using absolute path.
    [switch]$NoEnv,

    ### Do not automatically update user-wide PowerShell execution policy to allow running local scripts.
    [switch]$NoExecutionPolicy,

    ### Controls if all packages are enabled and exported.
    [ValidateSet("All", "Prompt", "None")]
    [string]$Enable = "Prompt"
)

Set-StrictMode -Version 3
$ErrorActionPreference = "Stop"

function abort($ErrorMsg) {
    # use ThrowTerminatingError instead of throw so that the error is rendered without removed newlines in ConciseView
    $PSCmdlet.ThrowTerminatingError([System.Management.Automation.ErrorRecord]::new($ErrorMsg, $null, "NotSpecified", $null))
}


$LoadedPogModule = Get-Module Pog
if ($LoadedPogModule -and $LoadedPogModule.Path -ne "$PSScriptRoot\app\Pog\Pog.psm1") {
    abort ("Another Pog instance ($($LoadedPogModule.Path)) is already loaded in the current PowerShell session. " +`
        "Please re-run '$PSCommandPath' in a new PowerShell session.")
}

if ($PSVersionTable.PSVersion -lt "5.0") {
    abort "Pog requires at least PowerShell 5."
}
if ($env:PROCESSOR_ARCHITECTURE -ne "AMD64") {
    abort "Pog requires x64. Sorry, ARM is not supported for now."
}


$SymlinkPath = "$env:TEMP\Pog-symlink-test-$(New-Guid)"
try {
    # try creating a symlink in TEMP to itself
    $null = cmd.exe /c mklink $SymlinkPath $SymlinkPath 2>$null
    $SymlinksAllowed = $LASTEXITCODE -eq 0
} catch {
    $SymlinksAllowed = $false
}
Remove-Item $SymlinkPath -ErrorAction Ignore

if (-not $SymlinksAllowed) {
    abort @"
Pog cannot create symbolic links, which are currently necessary for correct functionality.
To allow creating symbolic links, do any of the following and re-run '$PSCommandPath':
  1) Enable Developer Mode. (Settings -> Update & Security -> For developers -> Developer Mode)
  2) Allow your user account to create symbolic links using Group Policy.
     (Windows Settings -> Security Settings -> Local Policies -> User Rights Assignment -> Create symbolic links)
  3) Always run Pog as administrator.
"@
}


# first, start the BITS service in the background, which is used for downloading packages
# the user it likely to want to install a package after installing Pog, and BITS takes a bit of time to start up
$BitsJob = Start-Job {Start-Service BITS}
$null = Register-ObjectEvent -InputObject $BitsJob -EventName StateChanged -Action {
    # when the job finishes, automatically remove it
    Unregister-Event $EventSubscriber.SourceIdentifier
    Remove-Job $EventSubscriber.SourceIdentifier
    Remove-Job -Id $EventSubscriber.SourceObject.Id
}


# need to call unblock before importing any modules
Write-Host "Unblocking Pog PowerShell files..."
& {
    gi $PSScriptRoot/app/Pog/lib_compiled/*
    ls -Recurse $PSScriptRoot/app -File -Filter *.ps*1
 } | Unblock-File

# these modules only use library functions, so it is safe to import them even during setup
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
    Write-Host "Creating directory '$Dir'..."
    $null = New-Item -Type Directory -Force $Dir
}

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

# wrap all Pog invocations in a scriptblock; otherwise, in case when there's already one Pog installation
#  and we're setting up another one, PowerShell will notice that we're calling Pog while loading this script
#  and happily load the previously installed Pog module from PSModulePath; sigh

# setup 7zip
& {
    $7z = Get-PogPackage 7zip -ErrorAction Ignore
    if (-not $7z) {
        abort ("Could not find the '7zip' package, required for correct functioning of Pog. It should be distributed with Pog itself. " +`
            "Please install Pog from a release, not by cloning the repository directly.")
    }

    try {
        $7z | Enable-Pog -PassThru | Export-Pog
    } catch {
        abort "Failed to enable the '7zip' package, required for correct functioning of Pog: $_"
    }

    if (-not (Test-Path ([Pog.InternalState]::PathConfig.Path7Zip))) {
        abort "Setup of '7zip' was successful, but Pog cannot find the 7z.exe binary that should be provided by the package."
    }
}


# set up environment variables and execution policy
& {Enable-Pog Pog -PackageArguments @{
    NoEnv = $NoEnv
    NoExecutionPolicy = $NoExecutionPolicy
}}


# check if user already installed any packages; if so, prompt to enable & export them
#  so that everything is ready for the user in case of the portable scenario
$ExtraPackages = & {Get-PogPackage} | ? PackageName -notin "Pog", "7zip"
if ($ExtraPackages) {
    $ShouldEnable = switch ($Enable) {
        "All" {$true}
        "None" {$false}
        "Prompt" {
            $Title = "Enable installed packages"
            $Message = "If you are setting up Pog because you moved it to a new computer, the installed packages must be re-configured.`n" +`
                "Enable all $(@($ExtraPackages).Count) installed packages?"
            $Choice = try {
                $Host.UI.PromptForChoice($Title, $Message, @("&Yes", "&No"), 0)
            } catch [System.Management.Automation.PSInvalidOperationException] {
                1 # non-interactive mode, do not automatically enable
            }

            switch ($Choice) {
                0 {$true} # Yes
                1 { # No
                    Write-Host "Not enabling installed packages. To enable them manually, run:"
                    Write-Host "    Get-PogPackage | Enable-Pog -PassThru | Export-Pog" -ForegroundColor Green
                    $false
                }
            }
        }
    }

    if ($ShouldEnable) {
        # TODO: when some form of support for parallel setup is implemented, use it
        $ExtraPackages | Enable-Pog -PassThru | Export-Pog
    }
}


Write-Host ""
Write-Host "It seems Pog is now correctly set up. :)"
Write-Host ""
Write-Host "To install a package, run the following command (press Ctrl+Space for auto-complete):"
Write-Host '    pog <PackageName>' -ForegroundColor Green
Write-Host "To access the documentation, use the built-in PowerShell help system:"
Write-Host '    man about_Pog' -ForegroundColor Green