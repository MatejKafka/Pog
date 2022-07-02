# Requires -Version 7
using module ..\..\Confirmations.psm1
. $PSScriptRoot\..\..\lib\header.ps1

Export-ModuleMember -Function Confirm-Action


Export function ConfirmOverwrite {
	param(
			[Parameter(Mandatory)]
			[string]
		$Title,
			[Parameter(Mandatory)]
			[string]
		$Message
	)

	# user passed -AllowOverwrite
	if ($global:_Pog.InternalArguments.AllowOverwrite) {
		return $true
	}
	return Confirm-Action $Title $Message "_AllowOverwrite"
}
