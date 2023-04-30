Get-PogPackage | ? {
    if (-not $_.Version) {return $false}
    $r = Get-PogRepositoryPackage $_.ManifestName -ErrorAction Ignore
    return $r -and $r.Version -gt $_.Version
} | % {pog $_.ManifestName -TargetName $_.PackageName}