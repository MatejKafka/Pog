# 1) create the .template manifest
# 2) run this script
param(
        [Parameter(Mandatory)]
        [string]
    $PackageName
)

Import-Module Pog

if ([Pog.InternalState]::Repository -isnot [Pog.LocalRepository]) {
    throw "Only local repositories are supported."
}

$Package = [Pog.InternalState]::Repository.GetPackage($PackageName, $true, $true)
$PackagePath = $Package.Path

if (-not $Package.IsTemplated) {
    throw "Missing .template dir"
}

$TemplateKeys = [Pog.ManifestTemplateFile]::GetTemplateKeys($Package.TemplatePath)
if ("Url" -in $TemplateKeys) {
    throw "Url resolution is not yet supported."
}

ls $PackagePath/*/pog.psd1 -Exclude .template `
    | % {[Pog.PackageManifest]::new($_)} `
    | % {[ordered]@{Version = $_.Version.ToString(); Hash = $_.Install[0].ExpectedHash}} `
    | % {
        $ManifestPath = Resolve-VirtualPath "$PackagePath/$($_.Version).psd1"
        [Pog.ManifestTemplateFile]::SerializeSubstitutionFile($ManifestPath, $_)
        rm -Recurse "$PackagePath/$($_.Version)"
    }