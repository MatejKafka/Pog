@{
	ModuleVersion = '0.2.0'
	RootModule = 'Pkg.psm1'
	FunctionsToExport = @(
		"Export-PkgShortcutsToStartMenu"

		"Get-PkgManifest"
		"New-PkgManifest"
		"New-PkgDirectManifest"
		"Confirm-PkgPackage"
		"Confirm-PkgImportedManifest"
		
		"Get-PkgRoot"
		"New-PkgRoot"
		"Remove-PkgRoot"

		"Get-PkgPackage"
		"Get-PkgInstalledPackage"

		"Import-Pkg"
		"Enable-Pkg"
		"Install-Pkg"
	)
}