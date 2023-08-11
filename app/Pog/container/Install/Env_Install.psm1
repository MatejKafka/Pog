using module .\..\..\lib\Utils.psm1
# currently used by Install-FromUrl (TODO: port this dependency to C#)
using module .\LockedFiles.psm1
. $PSScriptRoot\..\..\lib\header.ps1

<# This function is called after the container setup is finished to run the passed manifest. #>
Export function __main {
	param($Manifest, $PackageArguments)

	if ($Manifest.Install -is [scriptblock]) {
		# run the installer scriptblock; this is only allowed for private manifests, repository packages must use a hashtable
		# see Env_Enable\__main for explanation of .GetNewClosure()
		& $Manifest.Install.GetNewClosure() @PackageArguments
	} else {
		# Install block is a hashtable of arguments to Install-FromUrl, or an array of these
		$Sources = foreach ($OrigEntry in $Manifest.Install) {
			# create a copy, do not modify the main manifest
			$Entry = $OrigEntry.Clone()
			# resolve SourceUrl/Url scriptblocks
			foreach ($Prop in @("SourceUrl", "Url")) {
				if ($Entry.ContainsKey($Prop) -and $Entry[$Prop] -is [scriptblock]) {
					# see Env_Enable\__main for explanation of .GetNewClosure()
					$Entry[$Prop] = & $Entry[$Prop].GetNewClosure()
				}
			}
			# we need [pscustomobject], otherwise piping to Install-FromUrl wouldn't work
			# (https://github.com/PowerShell/PowerShell/issues/13981)
			[pscustomobject]$Entry
		}

		# Install-FromUrl is implemented in Pog.dll
		$Sources | Install-FromUrl
	}
}

<# This function is called after __main finishes, even if it fails or gets interrupted. #>
Export function __cleanup {
	# nothing for now
}