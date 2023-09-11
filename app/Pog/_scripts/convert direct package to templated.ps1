Import-Module Pog

# 1) cd to the package directory in the repo
# 2) create the .template manifest
# 3) run this script
ls */pog.psd1 -Exclude .template `
    | % {[Pog.PackageManifest]::new($_)} `
    | % {[ordered]@{Version = $_.Version.ToString(); Hash = $_.Install[0].ExpectedHash}} `
    | % {
        [Pog.ManifestTemplateFile]::SerializeSubstitutionFile((Resolve-VirtualPath "$($_.Version).psd1"), $_)
        rm -Recurse $_.Version
    }