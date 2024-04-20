param([switch]$ListOnly, [switch]$NoManifestCheck)

$ImportedPackages = Get-PogPackage | ? {$_.Version -and $_.ManifestName}

$RepositoryPackageMap = @{}
$ImportedPackages | % ManifestName | select -Unique
    | Get-PogRepositoryPackage -LoadManifest:(-not $NoManifestCheck) -ErrorAction Ignore
    | % {
        $RepositoryPackageMap[$_.PackageName] = $_
    }

$SelectedPackages = $ImportedPackages
    | % {
        $r = $RepositoryPackageMap[$_.ManifestName]
        if (-not $r) {
            return
        }

        if ($r.Version -gt $_.Version -or (-not $NoManifestCheck -and $r.Version -eq $_.Version -and -not $r.MatchesImportedManifest($_))) {
            return [pscustomobject]@{
                PackageName = $_.PackageName
                CurrentVersion = $_.Version
                LatestVersion = $r.Version
                Target = $_
            }
        }
    }
    | Out-GridView -PassThru -Title "Outdated packages"
    | % Target

if ($ListOnly) {
    return $SelectedPackages
} else {
    $SelectedPackages | pog -Force <# -Force because user already confirmed the update #>
}