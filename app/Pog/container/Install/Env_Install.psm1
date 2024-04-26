using module .\..\..\lib\Utils.psm1
# currently used by Install-FromUrl (TODO: port this dependency to C#)
using module .\LockedFiles.psm1
. $PSScriptRoot\..\..\lib\header.ps1

<# This function is called after the container setup is finished to run the passed manifest. #>
Export function __main {
	param([Pog.PackageManifest]$Manifest, $PackageArguments)

	$Manifest.Install | Install-FromUrl <# Install-FromUrl is implemented in Pog.dll #>
}
