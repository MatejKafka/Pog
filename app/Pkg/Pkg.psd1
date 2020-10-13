@{
	ModuleVersion = '0.2.0'
	RootModule = 'Pkg.psm1'
	FunctionsToExport = @(
		"Export-PkgShortcutsToStartMenu"
		"New-PkgManifest"
		
		"Get-PkgRoot"
		"New-PkgRoot"
		"Remove-PkgRoot"
		
		"Enable-Pkg"
		"Install-Pkg"
	)
}