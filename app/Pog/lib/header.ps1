# Requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# pass `-ErrorAction Stop` to all commands; this is a second line of defence
#  after `$ErrorActionPreference` and ideally shouldn't be needed, but in some
#  contexts (e.g. apparently when calling $PSCmdlet.WriteError), it is necessary
#  to locally override $ErrorActionPreference
$script:PSDefaultParameterValues = @{
	"*:ErrorAction" = "Stop"
}

# all exports are done using the Export hacky fn
Export-ModuleMember

if (-not (Test-Path Env:POG_DEBUG)) {
	Import-Module $PSScriptRoot\..\lib_compiled\Pog.dll
} else {
	# load debug build of the compiled library
	Import-Module $PSScriptRoot\..\lib_compiled\Pog_Debug.dll
}


function Export {
	param (
			[Parameter(Mandatory)]
			[ValidateSet("function", "variable", "alias")]
		$Type,
			[Parameter(Mandatory)]
			[string]
		$Name,
			[Parameter(Mandatory)]
		$Value
	)

	switch ($Type) {
		"function" {
			Set-Item "function:script:$Name" $Value
			Export-ModuleMember $Name
		}
		"variable" {
			Set-Variable -Scope Script $Name $Value
			Export-ModuleMember -Variable $Name
		}
		"alias" {
			New-Alias -Scope Script $Name $Value
			Export-ModuleMember -Alias $Name
		}
	}
}
