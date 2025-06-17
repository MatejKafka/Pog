Set-StrictMode -Version 3
$ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop

# ensure that the Pog compiled library is loaded everywhere
& {
	# check if Pog.dll is already loaded in the process
	$PogSource = try {[Pog.InternalState].Assembly.Location} catch {}

	if ($PogSource -and -not $PogSource.StartsWith($PSScriptRoot)) {
		throw "Conflicting Pog.dll from another location already loaded: $PogSource"
	}

	if (-not $PogSource) {
		# not yet loaded
		if (-not (Test-Path Env:POG_DEBUG)) {
			$PogSource = "$PSScriptRoot\lib_compiled\Pog.dll"
		} else {
			# load debug build of the compiled library
			$PogSource = "$PSScriptRoot\lib_compiled\Pog\bin\Debug\netstandard2.0\Pog.dll"
		}
	}

	Import-Module $PogSource
}