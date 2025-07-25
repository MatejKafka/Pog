. $PSScriptRoot\..\SetupTestEnvironment.ps1 @Args

$ManifestTemplate = @'
@{
    Private = $true
    Enable = {
        # export any binary, not important
        Export-Shortcut '{{EXPORT}}' (gcm cmd.exe).Path `
            -Arguments /c, "echo TARGET INVOKED: %X%" `
            -Environment $(if ('{{ENV_X}}') {
                @{X = '{{ENV_X}}'}
            } else {
                $null
            })
    }
}
'@

function Set-Manifest($Path, $ExportName, $EnvX = "") {
    RenderTemplate $ManifestTemplate $Path @{
        EXPORT = $ExportName
        ENV_X = $EnvX
    }
}

function test($PackageName) {
    Enable-Pog $PackageName
    # invoke each exported shortcut to check that it works
    # we use `cmd` because it correctly routes the .lnk invocation output and waits for completion (unlike `Start-Process`)
    Get-Pog $PackageName | % ExportedShortcuts | % {$_.Name + ": " + (cmd /c $_.FullName).Trim()}
}


# setup package root
$Root = ".\root"
CreatePackageRoots $Root

$null = mkdir $Root\test-noenv, $Root\test-env


Set-Manifest $Root\test-noenv\pog.psd1 test
Set-Manifest $Root\test-env\pog.psd1 test -EnvX "env val"

title "Export noenv"
test test-noenv

title "Export env"
test test-env

title "Export noenv (re-run)"
test test-noenv

title "Export env (re-run)"
test test-env

Set-Manifest $Root\test-noenv\pog.psd1 TEST
Set-Manifest $Root\test-env\pog.psd1 TEST -EnvX "env val"

title "Export noenv with changed casing"
test test-noenv

title "Export env with changed casing"
test test-env

Set-Manifest $Root\test-env\pog.psd1 TEST -EnvX "env val 2"

title "Export env with internal shim changes"
test test-env