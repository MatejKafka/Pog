# NOTE: this script is used in CI
### Creates a new Pog release from the latest commit.
param(
    [string]$Version = "HEAD",
    ### Path at which the resulting .zip release archive is created.
    ### Defaults to "_releases/Pog-v$Version.zip" in the repository root.
    [string]$ReleasePath,
    ### Use the working tree state instead of the latest HEAD.
    [switch]$WorkingTree,
    ### Run the resulting release in Windows Sandbox.
    [switch]$Run
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Resolve-Path (git -C $PSScriptRoot rev-parse --show-toplevel)
$TmpDir = "$($env:TEMP)/PogRelease-$(New-Guid)"

$ReleasePath = if ($ReleasePath) {
    [System.IO.Path]::Combine($PWD, $ReleasePath)
} else {
    "$Root/_releases/Pog-v$Version.zip"
}

$OrigWd = Get-Location
try {
    $null = mkdir -Force $TmpDir
    cd $TmpDir

    @(
        "7zip/app"
        "7zip/pog.psd1"
    ) | % {
        cp -Recurse $Root/../$_ $_
    }

    $Ref = if ($WorkingTree) {git -C $Root stash create} else {"HEAD"}
    git -C $Root archive --format zip --output "$TmpDir/Pog.zip" $Ref

    Expand-Archive Pog.zip Pog
    rm Pog.zip

    cd ./Pog

    # need to store this one before we delete it
    $GhActionYaml = Get-Content -Raw ./.github/actions/install-pog/action.yml

    rm -Recurse @(
        ".github"
        ".gitignore"
        "README.md"
        "app/Pog/_knowledge"
        "app/Pog/_scripts"
        "app/Pog/tests"
        "app/Pog/PSScriptAnalyzerSettings.psd1"
    )

    $SrcLibCompiled = "$Root/app/Pog/lib_compiled"
    rm -Recurse app/Pog/lib_compiled/*
    # copy Pog binaries and vc redist
    @("Pog.dll", "Pog.dll-Help.xml", "PogShimTemplate.exe", "vc_redist") `
        | % {cp -Recurse $SrcLibCompiled/$_ app/Pog/lib_compiled/$_}


    # validate manifest versions
    $PogPSModuleVersion = (Import-PowerShellDataFile ./app/Pog/Pog.psd1).ModuleVersion
    $PogUtilsPSModuleVersion = (Import-PowerShellDataFile ./app/Pog.Utils/Pog.Utils.psd1).ModuleVersion
    $PogPackageVersion = (Import-PowerShellDataFile ./pog.psd1).Version
    $PogDllVersion = (Get-Item ./app/Pog/lib_compiled/Pog.dll).VersionInfo.ProductVersion
    $GhActionVersion = if ($GhActionYaml -match "\s+default: (\d+\.\d+\.\d+)`n") {
        [version]$Matches[1]
    } else {
        throw "Could not parse 'install-pog' GitHub Action Pog version."
    }

    $CheckVersion = if ($Version -ne "HEAD") {$Version} else {$PogPSModuleVersion}
    if ($PogPSModuleVersion -ne $CheckVersion) {
        throw "Pog.psd1 PowerShell module version does not match."
    }
    if ($PogUtilsPSModuleVersion -ne $CheckVersion) {
        throw "Pog.Utils.psd1 PowerShell module version does not match."
    }
    if ($PogPackageVersion -ne $CheckVersion) {
        throw "pog.psd1 Pog package manifest version does not match."
    }
    if ($PogDllVersion -ne $CheckVersion) {
        throw "Pog.dll product version does not match."
    }
    if ($GhActionVersion -ne $CheckVersion) {
        throw "GitHub Action Pog download version does not match."
    }

    cd $TmpDir

    $ReleaseArchive = Compress-Archive * $ReleasePath -Force -PassThru
} finally {
    cd $OrigWd
    rm -Recurse $TmpDir
}

echo $ReleaseArchive

if ($Run) {
    Write-Information "Running Pog in Windows Sandbox..."
    & $PSScriptRoot/run-release.ps1 $ReleaseArchive
}