<# Creates a new Pog release from latest commit, storing it in _releases dir in the root of the repository. #>
param([Parameter(Mandatory)][string]$Version, [switch]$WorkingTree, [switch]$Run)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Resolve-Path (git -C $PSScriptRoot rev-parse --show-toplevel)

$OrigWd = Get-Location
try {
    $null = mkdir -Force $Root/_releases/tmp
    cd $Root/_releases/tmp

    @(
        "7zip/app"
        "7zip/pog.psd1"
    ) | % {
        cp -Recurse $Root/../$_ $_
    }

    $Ref = if ($WorkingTree) {git stash create} else {"HEAD"}
    git -C $Root archive --format zip --output _releases/tmp/Pog.zip $Ref

    Expand-Archive Pog.zip Pog
    rm Pog.zip

    cd Pog

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
    @("Pog.dll", "Pog.dll-Help.xml", "PogExecutableStubTemplate.exe", "vc_redist") `
        | % {cp -Recurse $SrcLibCompiled/$_ app/Pog/lib_compiled/$_}


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


    cd $Root/_releases/tmp

    Compress-Archive * ../Pog-v${Version}.zip -Force
} finally {
    cd $OrigWd
    rm -Recurse $Root/_releases/tmp
}

if ($Run) {
    Write-Information "Running Pog in Windows Sandbox..."
    & $PSScriptRoot/run-release.ps1 $Version
}