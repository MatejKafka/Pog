using module ..\TestEnvironmentSetup.psm1
param([Parameter(Mandatory)][string]$TestDirPath)

. $PSScriptRoot\..\..\lib\header.ps1
$InformationPreference = "Continue"

function title($Title) {
    Write-Host "--- $Title ---" -ForegroundColor White
}

$ManifestTemplate = @'
@{
    Private = $true
    Enable = {
        # export any binary, not important
        Export-Shortcut '{{EXPORT}}' (gcm cmd.exe).Path -Environment $(if ([bool]'{{SHIM}}') {
            @{X = "y"}
        } else {
            $null
        })
    }
}
'@

function Set-Manifest($Path, $ExportName, [switch]$Shim) {
    RenderTemplate $ManifestTemplate $Path @{
        EXPORT = $ExportName
        SHIM = if ($Shim) {'$true'} else {'$false'}
    }
}

function test($PackageName) {
    Enable-Pog $PackageName
    Get-PogPackage $PackageName | % ExportedShortcuts | % Name
}


$TEST_DIR = SetupNewPogTestDir $TestDirPath

# setup package root
$Root = "$TEST_DIR\root"
CreatePackageRoots $Root

$null = mkdir $Root\test-direct, $Root\test-shim


Set-Manifest $Root\test-direct\pog.psd1 test
Set-Manifest $Root\test-shim\pog.psd1 test -Shim

title "Export direct"
test test-direct

title "Export with hidden stub"
test test-shim

title "Export direct (re-run)"
test test-direct

title "Export with hidden stub (re-run)"
test test-shim

Set-Manifest $Root\test-direct\pog.psd1 TEST
Set-Manifest $Root\test-shim\pog.psd1 TEST -Shim

title "Export direct with changed casing"
test test-direct

title "Export shim with changed casing"
test test-shim
