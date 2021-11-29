# Requires -Version 7
. $PSScriptRoot\..\..\lib\header.ps1

Import-Module $PSScriptRoot\..\..\Confirmations

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
	if ($global:_InternalArgs.AllowOverwrite) {
		return $true
	}
	return Confirm-Action $Title $Message "_AllowOverwrite"
}