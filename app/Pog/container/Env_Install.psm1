using module ..\lib\Utils.psm1
# currently used by Install-FromUrl (TODO: port this dependency to C#)
using module .\LockedFiles.psm1
. $PSScriptRoot\..\lib\header.ps1

function __main {
	param([Pog.PackageManifest]$Manifest)

	$Manifest.Install | Install-FromUrl <# Install-FromUrl is implemented in Pog.dll #>
}

Export-ModuleMember -Function __main