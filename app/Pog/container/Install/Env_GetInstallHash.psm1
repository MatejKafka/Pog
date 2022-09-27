# Requires -Version 7
. $PSScriptRoot\..\..\lib\header.ps1


<# This function is called after the container setup is finished to run the passed manifest. #>
Export function __main {
    param($Manifest, $PackageArguments)

	$Installer = $Manifest.Install
	if ($Installer -is [scriptblock]) {
		throw "Source file hash retrieval for scriptblock-based installers is not supported."
	}

    # FIXME: copy parameters from Install-FromUrl, so that we can reuse its validation
    #  and ensure the arguments are consistent for both environments
    $Url = if ($Installer.ContainsKey("SourceUrl")) {
        if ($Installer.SourceUrl -is [scriptblock]) {& $Installer.SourceUrl} else {$Installer.SourceUrl}
    } else {
        if ($Installer.Url -is [scriptblock]) {& $Installer.Url} else {$Installer.Url}
    }

    $ExpectedHash = if ($Installer.ContainsKey("ExpectedHash")) {$Installer.ExpectedHash}
        elseif ($Installer.ContainsKey("Hash")) {$Installer.Hash}
        else {$null}

    $UserAgent = if ($Installer.ContainsKey("UserAgent")) {$Installer.UserAgent}
        else {[Pog.Commands.DownloadParameters+UserAgentType]::PowerShell}
    $DownloadParams = [Pog.Commands.DownloadParameters]::new($UserAgent)

    $LockedFile = Invoke-FileDownload $Url -DownloadParameters $DownloadParams -StoreInCache -Package $global:_Pog.Package
    # we don't need the lock, we're only interested in the hash
    $LockedFile.Unlock()
    $Hash = $LockedFile.EntryKey
    $Hash | Set-Clipboard

    Write-Host ""
    Write-Host "Hash for the file at '$Url' (copied to clipboard):"
    Write-Host "$Hash"

    if ($ExpectedHash) {
        if ($ExpectedHash -eq $Hash) {
            Write-Host -ForegroundColor Green "Matches the expected hash specified in the manifest."
        } else {
            throw "The retrieved hash does not match the expected hash specified in the manifest (expected: '$ExpectedHash')."
        }
    }
}

<# This function is called after __main finishes. #>
Export function __cleanup {
	# nothing for now
}
