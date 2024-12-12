using module ..\Utils.psm1
. $PSScriptRoot\..\header.ps1

function __main {
	param([Pog.PackageManifest]$Manifest)

	$Manifest.Install | Install-FromUrl <# Install-FromUrl is implemented in Pog.dll #>
}

Export-ModuleMember -Function __main