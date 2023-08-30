@{
	RootModule = 'Pog.psm1'
	ModuleVersion = '0.5.0'
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
		'Disable-Pog'
		'Uninstall-Pog'

		'Get-PogRepositoryPackage'
		'Get-PogPackage'

		'Clear-PogDownloadCache'

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

		'Edit-PogRootList'
	)
}