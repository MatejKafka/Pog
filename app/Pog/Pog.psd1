# Requires -Version 7
@{
	ModuleVersion = '0.2.0'
	RootModule = 'Pog.psm1'

	DefaultCommandPrefix = 'Pog'

	# https://github.com/PowerShell/PowerShell/issues/12858
	#  as a workaround, we "export" both versions, so that the commands are correctly auto-loaded
	FunctionsToExport = @(
		"Import-"
		"Import-Pog"
		"Enable-"
		"Enable-Pog"
		"Install-"
		"Install-Pog"

		"Export-ShortcutsToStartMenu"
		"Export-PogShortcutsToStartMenu"

		"Show-ManifestHash"
		"Show-PogManifestHash"
		"New-Manifest"
		"New-PogManifest"
		"New-DirectManifest"
		"New-PogDirectManifest"
		"Update-Manifest"
		"Update-PogManifest"

		"Confirm-RepositoryPackage"
		"Confirm-PogRepositoryPackage"
		"Confirm-Package"
		"Confirm-PogPackage"
		"Get-RepositoryPackage"
		"Get-PogRepositoryPackage"
		"Get-Package"
		"Get-PogPackage"

		"Clear-DownloadCache"
		"Clear-PogDownloadCache"

		"Get-Root"
		"Get-PogRoot"
		"Edit-RootList"
		"Edit-PogRootList"
	)
}