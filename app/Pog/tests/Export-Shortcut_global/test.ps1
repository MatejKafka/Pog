. $PSScriptRoot\..\SetupTestEnvironment.ps1 @Args

$ManifestTemplate = @'
@{
    Private = $true
    Enable = {
        # export any binary, not important
        Export-Shortcut '{{EXPORT}}' (gcm cmd.exe).Path `
            -Arguments /c, "echo TARGET INVOKED: %X%" `
            -Description $(if ('{{DESCRIPTION}}') {'{{DESCRIPTION}}'} else {$null})
    }
}
'@

function Set-Manifest($Path, $ExportName, $Description = $null) {
    RenderTemplate $ManifestTemplate $Path @{
        EXPORT = $ExportName
        DESCRIPTION = $Description
    }
}

function DumpExportedShortcuts {
    $Shell = New-Object -ComObject WScript.Shell
    ls ([Pog.InternalState]::PathConfig.ExportedShortcutDir) | % {
        $s = $Shell.CreateShortcut($_.FullName)
        $_.FullName + " -> " + $s.TargetPath + ": " + $s.Description
    }
}

function test($PackageName, $Description, [switch]$Export) {
    Set-Manifest $Root\$PackageName\pog.psd1 test $Description
    Enable-Pog $PackageName
    if ($Export) {
        Export-Pog $PackageName
    }
    DumpExportedShortcuts
}


# setup package root
$Root = ".\root"
CreatePackageRoots $Root

$null = mkdir $Root\package1, $Root\package2

$InformationPreference = "SilentlyContinue"

title "Export 1"
test package1 "description-package1" -Export

title "Export 2"
test package2 "description-package2" -Export

title "Update 1"
test package1 "description-package1-updated"

title "Update 2"
test package2 "description-package2-updated"

title "Remove 1"
# this should preserve the shortcut...
Set-Content $Root\package1\pog.psd1 '@{Private = $true; Enable = {}}'
Enable-Pog package1
DumpExportedShortcuts

title "Remove 2"
# ...and this should remove it
Set-Content $Root\package2\pog.psd1 '@{Private = $true; Enable = {}}'
Enable-Pog package2
DumpExportedShortcuts