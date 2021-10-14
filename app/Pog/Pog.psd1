# Requires -Version 7
@{
	ModuleVersion = '0.2.0'
	RootModule = 'Pog.psm1'

	DefaultCommandPrefix = 'Pog'

	FunctionsToExport = @(
		"Export-ShortcutsToStartMenu"

		"Get-Manifest"
		"New-Manifest"
		"New-DirectManifest"
		"Update-Manifest"

		"Confirm-RepositoryPackage"
		"Confirm-Package"
		"Get-RepositoryPackage"
		"Get-Package"

		"Clear-DownloadCache"

		"Get-Root"
		"New-Root"
		"Remove-Root"

		"Import-"
		"Enable-"
		"Install-"


		# https://github.com/PowerShell/PowerShell/issues/12858
		#  as a workaround, we "export" both versions, so that the commands are correctly auto-loaded
		"Export-PogShortcutsToStartMenu"
		"Get-PogManifest"
		"New-PogManifest"
		"New-PogDirectManifest"
		"Update-PogManifest"
		"Confirm-PogRepositoryPackage"
		"Confirm-PogPackage"
		"Get-PogRepositoryPackage"
		"Get-PogPackage"
		"Clear-PogDownloadCache"
		"Get-PogRoot"
		"New-PogRoot"
		"Remove-PogRoot"
		"Import-Pog"
		"Enable-Pog"
		"Install-Pog"
	)
}