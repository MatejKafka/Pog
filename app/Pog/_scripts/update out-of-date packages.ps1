Get-PogPackage
    | ? {
        if (-not $_.Version -or -not $_.ManifestName) {return $false}
        $r = Get-PogRepositoryPackage $_.ManifestName -ErrorAction Ignore
        return $r -and $r.Version -gt $_.Version
    }
    | Out-GridView -PassThru -Title "Outdated packages"
    | pog -Force <# -Force because user already confirmed the update #>