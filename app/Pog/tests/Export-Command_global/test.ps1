param([Parameter(Mandatory)][string]$TestDirPath)
. $PSScriptRoot\..\TestEnvironmentSetup.ps1

$ManifestTemplate = @'
@{
    Private = $true
    Enable = {
        # export any binary, not important
        Export-Command '{{EXPORT}}' (gcm cmd.exe).Path
    }
}
'@

function Set-Manifest($Path, $ExportName) {
    RenderTemplate $ManifestTemplate $Path @{
        EXPORT = $ExportName
    }
}

function DumpExportedCommands {
    ls ([Pog.InternalState]::PathConfig.ExportedCommandDir) | % {
        $_.FullName + " -> " + $_.Target
    }
}

function test($PackageName, [switch]$Export) {
    Set-Manifest $Root\$PackageName\pog.psd1 test
    Enable-Pog $PackageName
    if ($Export) {
        Export-Pog $PackageName
    }
    DumpExportedCommands
}


$TEST_DIR = SetupNewPogTestDir $TestDirPath

# setup package root
$Root = "$TEST_DIR\root"
CreatePackageRoots $Root

$null = mkdir $Root\package1, $Root\package2

$InformationPreference = "SilentlyContinue"

title "Export 1"
test package1 -Export

title "Export 2"
test package2 -Export

title "Update 1"
test package1

title "Update 2"
test package2

title "Remove 1"
# this should preserve the command...
Set-Content $Root\package1\pog.psd1 '@{Private = $true; Enable = {}}'
Enable-Pog package1
DumpExportedCommands

title "Remove 2"
# ...and this should remove it
Set-Content $Root\package2\pog.psd1 '@{Private = $true; Enable = {}}'
Enable-Pog package2
DumpExportedCommands