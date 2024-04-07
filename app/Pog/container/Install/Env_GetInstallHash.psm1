. $PSScriptRoot\..\..\lib\header.ps1


<# This function is called after the container setup is finished to run the passed manifest. #>
Export function __main {
    param([Pog.PackageManifest]$Manifest, $PackageArguments)

    $Hashes = @()
    $First = $true
	foreach ($Installer in $Manifest.Install) {
        if (-not $First) {
            Write-Host ""
        }
        $First = $false

        $Url = $Installer.ResolveUrl()
        $Hash = Get-CachedUrlHash $Url -UserAgent $Installer.UserAgent
        $Hashes += $Hash

        Write-Host "Hash for the file at '$Url' (copied to clipboard):"
        Write-Host $Hash -ForegroundColor White

        # we cannot use Set-Clipboard in the container in powershell.exe (it throws an exception that it needs STA for OLE)
        # this ported class from pwsh has a workaround
        [Pog.Native.Clipboard]::SetText($Hashes -join "`n")

        if ($Installer.ExpectedHash) {
            if ($Installer.ExpectedHash -eq $Hash) {
                Write-Host "Matches the expected hash specified in the manifest." -ForegroundColor Green
            } else {
                throw "The retrieved hash does not match the expected hash specified in the manifest (expected: '$($Installer.ExpectedHash)')."
            }
        }
    }
}

<# This function is called after __main finishes. #>
Export function __cleanup {
	# nothing for now
}
