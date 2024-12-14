using module ..\Utils.psm1
. $PSScriptRoot\..\header.ps1

function __main {
	param([Pog.PackageManifest]$Manifest)

	# Install-FromUrl is implemented in Pog.dll
	Install-FromUrl $Manifest.Install
}

Export-ModuleMember -Function __main