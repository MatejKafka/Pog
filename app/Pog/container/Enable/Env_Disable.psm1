using module ..\..\Paths.psm1
. $PSScriptRoot\..\..\lib\header.ps1

<# This function is called after the container setup is finished to run the Disable script. #>
Export function __main {
    # __main must NOT have [CmdletBinding()], otherwise we lose error message position from the manifest scriptblock
	param([Pog.PackageManifest]$Manifest, $PackageArguments)

    # invoke the scriptblock
	# without .GetNewClosure(), the script block would see our internal module functions, probably because
	#  it would be automatically bound to our SessionState; not really sure why GetNewClosure() binds it to
	#  a different scope
	& $Manifest.Disable.GetNewClosure()
}

<# This function is called after __main finishes, even if it fails or gets interrupted. #>
Export function __cleanup {
	# nothing for now
}