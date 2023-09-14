Get-PogPackage
    | ? {
        if (-not $_.Version -or -not $_.ManifestName) {return $false}
        $r = Get-PogRepositoryPackage $_.ManifestName $_.Version -ErrorAction Ignore
        return $r -and -not $_.MatchesRepositoryManifest($r)
    }
    | Out-GridView -PassThru -Title "Mismatched packages"
    | % {
        pog $_.ManifestName $_.Version `
            -TargetName $_.PackageName -TargetPackageRoot (Split-Path $_.Path) `
            -Force <# -Force because user already confirmed the update #>
    }