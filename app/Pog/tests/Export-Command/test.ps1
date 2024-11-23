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
        Export-Command '{{EXPORT}}' (gcm cmd.exe).Path -Symlink:([bool]'{{SYMLINK}}')
    }
}
'@

function Set-Manifest($Path, $ExportName, [switch]$Symlink) {
    RenderTemplate $ManifestTemplate $Path @{
        EXPORT = $ExportName
        SYMLINK = if ($Symlink) {'$true'} else {'$false'}
    }
}

function test($PackageName) {
    Enable-Pog $PackageName
    Get-PogPackage $PackageName | % ExportedCommands | % Name
}


$TEST_DIR = SetupNewPogTestDir $TestDirPath

# setup package root
$Root = "$TEST_DIR\root"
CreatePackageRoots $Root

$null = mkdir $Root\test-shim, $Root\test-symlink


Set-Manifest $Root\test-shim\pog.psd1 test
Set-Manifest $Root\test-symlink\pog.psd1 test -Symlink

title "Export shim"
test test-shim

title "Export symlink"
test test-symlink

title "Export shim (re-run)"
test test-shim

title "Export symlink (re-run)"
test test-symlink

Set-Manifest $Root\test-shim\pog.psd1 TEST
Set-Manifest $Root\test-symlink\pog.psd1 TEST -Symlink

title "Export shim with changed casing"
test test-shim

title "Export symlink with changed casing"
test test-symlink
