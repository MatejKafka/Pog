Get-PogPackage
    | % {
        if (-not $_.Version -or -not $_.ManifestName) {return}
        $r = Get-PogRepositoryPackage $_.ManifestName -ErrorAction Ignore
        if ($r -and $r.Version -gt $_.Version) {
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
    | pog -Force <# -Force because user already confirmed the update #>