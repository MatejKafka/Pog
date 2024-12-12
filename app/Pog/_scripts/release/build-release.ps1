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


    if ($Version -ne "HEAD") {
        # validate manifest versions
        $PogPackageVersion = (Import-PowerShellDataFile ./app/Pog/Pog.psd1).ModuleVersion
        $PogPSModuleVersion = (Import-PowerShellDataFile ./pog.psd1).Version
        $PogDllVersion = (gi ./app/Pog/lib_compiled/Pog.dll).VersionInfo.ProductVersion

        if ($PogPSModuleVersion -ne $Version) {
            throw "Pog.psd1 PS module version does not match"
        }
        if ($PogPackageVersion -ne $Version) {
            throw "pog.psd1 package manifest version does not match"
        }
        if ($PogDllVersion -ne $Version) {
            throw "Pog.dll product version does not match"
        }
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
