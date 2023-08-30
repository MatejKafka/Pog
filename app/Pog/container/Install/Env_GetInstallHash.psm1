. $PSScriptRoot\..\..\lib\header.ps1


<# This function is called after the container setup is finished to run the passed manifest. #>
Export function __main {
    param([Pog.PackageManifest]$Manifest, $PackageArguments)

    $First = $true
	foreach ($Installer in $Manifest.Install) {
        if (-not $First) {
            Write-Host ""
        }
        $First = $false

        $Url = $Installer.ResolveUrl()
        $DownloadParams = [Pog.Commands.Internal.DownloadParameters]::new($Installer.UserAgent)

        $LockedFile = Invoke-FileDownload $Url -DownloadParameters $DownloadParams -StoreInCache -Package $global:_Pog.Package
        # we don't need the lock, we're only interested in the hash
        $LockedFile.Unlock()
        $Hash = $LockedFile.EntryKey
        # output the hash, since we cannot use Set-Clipboard in the container in powershell.exe (it uses MTA, STA is needed for OLE calls)
        echo $Hash

        Write-Host "Hash for the file at '$Url' (copied to clipboard):"
        Write-Host "$Hash" -ForegroundColor White

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
