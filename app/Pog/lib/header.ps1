Set-StrictMode -Version 3
$ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop

# ensure that the Pog compiled library is loaded everywhere
if (-not (Test-Path Env:POG_DEBUG)) {
	Import-Module $PSScriptRoot\..\lib_compiled\Pog.dll
} else {
	# load debug build of the compiled library
	Import-Module $PSScriptRoot\..\lib_compiled\Pog\bin\Debug\netstandard2.0\Pog.dll
}
