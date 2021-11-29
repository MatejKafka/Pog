# Requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# all exports are done using the Export hacky fn
Export-ModuleMember


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
			Set-item "function:script:$Name" $Value
			Export-ModuleMember $Name
		}
		"variable" {
			Set-Variable -Scope Script $Name $Value
			Export-ModuleMember -Variable $Name
		}
		"alias" {
			New-Alias $Name $Value
			Export-ModuleMember -Alias $Name
		}
	}
}