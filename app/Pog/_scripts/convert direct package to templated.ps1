param(
        [Parameter(Mandatory)]
        [string]
    $PackageName
)

Import-Module Pog

# 1) create the .template manifest
# 2) run this script
$PackagePath = [Pog.InternalState]::Repository.GetPackage($PackageName, $true, $true).Path

if (-not (Test-Path "$PackagePath/.template")) {
    throw "Missing .template dir"
}

ls $PackagePath/*/pog.psd1 -Exclude .template `
    | % {[Pog.PackageManifest]::new($_)} `
    | % {[ordered]@{Version = $_.Version.ToString(); Hash = $_.Install[0].ExpectedHash}} `
    | % {
        $ManifestPath = Resolve-VirtualPath "$PackagePath/$($_.Version).psd1"
        [Pog.ManifestTemplateFile]::SerializeSubstitutionFile($ManifestPath, $_)
        rm -Recurse "$PackagePath/$($_.Version)"
    }