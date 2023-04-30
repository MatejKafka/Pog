Get-PogRepositoryPackage -AllVersions
    | % {$Url = $_.Manifest.Raw.Install.Url; if ($Url -is [scriptblock]) {$this = $_.Manifest.Raw; . $Url} else {$Url}}
    | % -Parallel {$Url = $_; try {$null = Invoke-WebRequest $_ -Method Head} catch {"ERROR: $Url $_"}} -ThrottleLimit 50