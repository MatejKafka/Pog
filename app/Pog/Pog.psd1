@{
	RootModule = 'Pog.psm1'
	ModuleVersion = '0.4.0'
	GUID = 'decb807b-afa1-4111-ad81-bfe9aa7dd44d'
	Author = 'Matej Kafka'
	CompatiblePSEditions = @('Desktop', 'Core')

	# originally, `DefaultCommandPrefix = 'Pog'` was used and the internal commands were not prefixed with `Pog`,
	#  but DefaultCommandPrefix has two issues – autoloading breaks (https://github.com/PowerShell/PowerShell/issues/12858),
	#  and it does not seem possible to export unprefixed aliases

	VariablesToExport = @()
	AliasesToExport = @('pog')

	CmdletsToExport = @(
		'Install-Pog'
		'Export-Pog'

		'Get-PogRepositoryPackage'
		'Get-PogPackage'

		'Get-PogRoot'
	)

	FunctionsToExport = @(
		'Invoke-Pog'
		'Import-Pog'
		'Enable-Pog'

		'Update-PogManifest'
		'Show-PogManifestHash'
		'New-PogPackage'
		'New-PogImportedPackage'

		'Confirm-PogRepositoryPackage'
		'Confirm-PogPackage'

		'Clear-PogDownloadCache'

		'Edit-PogRootList'
	)
}