Get-PogRepositoryPackage -AllVersions
    | % {$_.Manifest.Install}
    | % ResolveUrl
    | % -ThrottleLimit 50 -Parallel {
        $Url = $_
        try {$null = Invoke-WebRequest $_ -Method Head} catch {"ERROR: $Url $_"}
    }