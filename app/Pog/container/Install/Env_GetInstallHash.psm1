# Requires -Version 7
. $PSScriptRoot\..\..\lib\header.ps1

Import-Module $PSScriptRoot\FileDownloader

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

    $UserAgent = if ($Installer.ContainsKey("UserAgent")) {$Installer.UserAgent}
        else {[UserAgentType]::PowerShell}

    Write-Information "Retrieving the file hash for '$Url'..."
    $Hash = Get-UrlFileHash $Url -DownloadParams @{UserAgent = $UserAgent} -ShouldCache
    Write-Host ""
    Write-Host "Hash for the file at '$Url' (copied to clipboard):"
    Write-Host "$Hash"
    $Hash | Set-Clipboard
}

<# This function is called after __main finishes. #>
Export function __cleanup {
	# nothing for now
}
