param([switch]$ListOnly)

$SelectedPackages = Get-PogPackage
    | % {
        if (-not $_.Version -or -not $_.ManifestName) {
            return
        }

        $r = Get-PogRepositoryPackage $_.ManifestName -ErrorAction Ignore
        if (-not $r) {
            return
        }

        if ($r.Version -gt $_.Version -or ($r.Version -eq $_.Version -and -not $r.MatchesImportedManifest($_))) {
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