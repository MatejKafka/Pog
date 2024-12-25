using module ..\Utils.psm1
. $PSScriptRoot\..\header.ps1

function __main {
	### .SYNOPSIS
	### This function is invoked when the Install container is started.
	param([Pog.PackageManifest]$Manifest)

	# Install-FromUrl is implemented in Pog.dll
	Install-FromUrl $Manifest.Install
}

Export-ModuleMember -Function __main