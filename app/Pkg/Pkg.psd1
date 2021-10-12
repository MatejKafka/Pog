# Requires -Version 7
@{
	ModuleVersion = '0.2.0'
	RootModule = 'Pkg.psm1'
	
	DefaultCommandPrefix = 'Pkg'
	
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
		
		"Get-Root"
		"New-Root"
		"Remove-Root"
		
		"Import-"
		"Enable-"
		"Install-"
		
		
		# https://github.com/PowerShell/PowerShell/issues/12858
		#  as a workaround, we "export" both versions, so that the commands are correctly auto-loaded
		"Export-PkgShortcutsToStartMenu"
		"Get-PkgManifest"
		"New-PkgManifest"
		"New-PkgDirectManifest"
		"Update-PkgManifest"
		"Confirm-PkgRepositoryPackage"
		"Confirm-PkgPackage"
		"Get-PkgRepositoryPackage"
		"Get-PkgPackage"
		"Get-PkgRoot"
		"New-PkgRoot"
		"Remove-PkgRoot"
		"Import-Pkg"
		"Enable-Pkg"
		"Install-Pkg"
	)
}