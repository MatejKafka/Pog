@{
	ModuleVersion = '0.2.0'
	RootModule = 'Pkg.psm1'
	FunctionsToExport = @(
		"Export-PkgShortcutsToStartMenu"
		"New-PkgManifest"
		"Copy-PkgManifestsToRepository"
		
		"Get-PkgRoot"
		"New-PkgRoot"
		"Remove-PkgRoot"

		"Initialize-PkgPackage"
		"Enable-Pkg"
		"Install-Pkg"
	)
}