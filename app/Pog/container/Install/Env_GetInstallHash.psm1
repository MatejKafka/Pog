# Requires -Version 7
. $PSScriptRoot\..\..\lib\header.ps1


<# This function is called after the container setup is finished to run the passed manifest. #>
Export function __main {
    param($Manifest, $PackageArguments)

    [string[]]$Hashes = @()

    $First = $true
	foreach ($Installer in $Manifest.Install) {
        if (-not $First) {
            Write-Host ""
        }
        $First = $false

        if ($Installer -is [scriptblock]) {
            throw "Source file hash retrieval for scriptblock-based installers is not supported."
        }

        # FIXME: copy parameters from Install-FromUrl, so that we can reuse its validation
        #  and ensure the arguments are consistent for both environments
        $Url = if ($Installer.ContainsKey("SourceUrl")) {
            # see Env_Enable\__main for explanation of .GetNewClosure()
            if ($Installer.SourceUrl -is [scriptblock]) {& $Installer.SourceUrl.GetNewClosure()} else {$Installer.SourceUrl}
        } else {
            # see Env_Enable\__main for explanation of .GetNewClosure()
            if ($Installer.Url -is [scriptblock]) {& $Installer.Url.GetNewClosure()} else {$Installer.Url}
        }

        $ExpectedHash = if ($Installer.ContainsKey("ExpectedHash")) {$Installer.ExpectedHash}
            elseif ($Installer.ContainsKey("Hash")) {$Installer.Hash}
            else {$null}

        $UserAgent = if ($Installer.ContainsKey("UserAgent")) {$Installer.UserAgent}
            else {[Pog.Commands.Internal.DownloadParameters+UserAgentType]::PowerShell}
        $DownloadParams = [Pog.Commands.Internal.DownloadParameters]::new($UserAgent)

        $LockedFile = Invoke-FileDownload $Url -DownloadParameters $DownloadParams -StoreInCache -Package $global:_Pog.Package
        # we don't need the lock, we're only interested in the hash
        $LockedFile.Unlock()
        $Hash = $LockedFile.EntryKey
        $Hashes += $Hash

        Write-Host "Hash for the file at '$Url' (copied to clipboard):"
        Write-Host "$Hash" -ForegroundColor White

        if ($ExpectedHash) {
            if ($ExpectedHash -eq $Hash) {
                Write-Host "Matches the expected hash specified in the manifest." -ForegroundColor Green
            } else {
                throw "The retrieved hash does not match the expected hash specified in the manifest (expected: '$ExpectedHash')."
            }
        }
    }

    $Hashes -join "`n" | Set-Clipboard
}

<# This function is called after __main finishes. #>
Export function __cleanup {
	# nothing for now
}
