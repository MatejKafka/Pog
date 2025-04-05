Find-Pog -AllVersions -LoadManifest
    | % {$_.Manifest.EvaluateInstallUrls($_)}
    | % -ThrottleLimit 50 -Parallel {
        $Url = $_
        try {$null = Invoke-WebRequest $_ -Method Head} catch {"ERROR: $Url $_"}
    }