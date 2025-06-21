. $PSScriptRoot\..\SetupTestEnvironment.ps1 @Args

function update($Title) {
    title $Title
    Update-PogRepository @Args | % {
        # render output into a string, powershell.exe has slightly different whitespace formatting for tables from pwsh.exe
        $_.PackageName + " v" + $_.Version + ": " + $(if ($_.Manifest) {$_.Manifest.Install.Url} else {"<no manifest>"})
    }

    title "list versions"
    Find-Pog package -AllVersions | % Version

    ""
}

CreateTemplateManifest 'package'
CreateGenerator 'package' {'1.2.3', '1.2.4'}

update "initial update"
update "re-run update (should not change anything)"

CreateGenerator 'package' {'1.2.3', '1.2.4', '1.2.5'}
update "add version 1.2.5 -ListOnly" -ListOnly
update "add version 1.2.5"

CreateGenerator 'package' {'1.2.3', '1.2.4', '1.2.5', '1.2.6'} -Url {"https://new.url/$_"}
update "add version 1.2.6 with new URL"

update "regenerate all manifests" -Force