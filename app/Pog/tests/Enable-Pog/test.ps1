. $PSScriptRoot\..\SetupTestEnvironment.ps1 @Args

# setup package root
$Root = ".\root"
CreatePackageRoots $Root

CreateImportedPackage $Root test - - @'
@{
    Private = $true
    Enable = {
        Write-Warning "warning"
        "warning streamed" | Write-Warning
        Write-Information "info"
        # pipeline input is only supported in pwsh, not powershell
        "info streamed" | % {Write-Information $_}
        Write-Verbose "verbose"
        "verbose streamed" | Write-Verbose
        Write-Debug "debug"
        "debug streamed" | Write-Debug
    }
}
'@

title "Log prefixing"
& {
    $VerbosePreference = "Continue"
    $DebugPreference = "Continue"
    Enable-Pog test
}
