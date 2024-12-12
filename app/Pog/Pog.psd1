@{
	RootModule = 'Pog.psm1'
	ModuleVersion = '0.10.0'
	GUID = 'decb807b-afa1-4111-ad81-bfe9aa7dd44d'
	Author = 'Matej Kafka'
	CompatiblePSEditions = @('Desktop', 'Core')

	# originally, `DefaultCommandPrefix = 'Pog'` was used and the internal commands were not prefixed with `Pog`,
	#  but DefaultCommandPrefix has two issues – autoloading breaks (https://github.com/PowerShell/PowerShell/issues/12858),
	#  and it does not seem possible to export unprefixed aliases

	VariablesToExport = @()

	AliasesToExport = @(
		'pog'
	)

	CmdletsToExport = @(
		'Invoke-Pog'
		'Import-Pog'
		'Install-Pog'
		'Enable-Pog'
		'Export-Pog'
		'Disable-Pog'
		'Uninstall-Pog'

		'Find-Pog'
		'Get-Pog'

		'Confirm-Pog'
		'Confirm-PogRepository'

		'Show-PogSourceHash'
		'Clear-PogDownloadCache'

		'Get-PogRoot'
	)

	FunctionsToExport = @(
		'Update-Pog'
		'Update-PogRepository'
		# FIXME: these two names are inconsistent with the other commnands (explicit "Package" suffix),
		#  but I could not figure out a better name
		'New-PogRepositoryPackage'
		'New-PogPackage'

		'Set-PogRepository'
		'Edit-PogRoot'
	)

	FormatsToProcess = 'Pog.Format.ps1xml'
}