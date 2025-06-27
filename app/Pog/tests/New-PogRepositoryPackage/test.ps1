. $PSScriptRoot\..\SetupTestEnvironment.ps1 @Args

function test($Name) {
    $p = New-PogRepositoryPackage $Name @Args
    $c = [Pog.InternalState]::Repository.GetPackage($Name, $true, $true)

    if ($c.IsTemplated) {
        "TEMPLATE:"
        Get-Content $c.TemplatePath
    }

    if ($c.HasGenerator) {
        "GENERATOR:"
        Get-Content $c.GeneratorPath
    }

    "MANIFESTS:"
    $p | % {$_.Manifest.RawString}
}

title "Generated package with version"
test "generated" 1.2.3 -Type Generated

title "Templated package with version"
test "templated" 1.2.3 -Type Templated

title "Direct package with version"
test "direct" 1.2.3 -Type Direct

title "Direct package with multiple versions"
test "direct2" 1.2.3, 1.2.4 -Type Direct

title "Generated package with no version"
test "generated2" -Type Generated

title "Templated package with no version"
test "templated2" -Type Templated
