# Requires -Version 7
. $PSScriptRoot\lib\header.ps1

$PATH_CONFIG = [Pog.PathConfig]::new((Resolve-Path "$PSScriptRoot\..\.."))
$REPOSITORY = [Pog.Repository]::new($PATH_CONFIG.ManifestRepositoryDir)
$PACKAGE_ROOTS = [Pog.PackageRoots]::new($PATH_CONFIG.PackageRoots)
Export-ModuleMember -Variable PATH_CONFIG, REPOSITORY, PACKAGE_ROOTS

# warn about missing package roots
foreach ($r in $PATH_CONFIG.PackageRoots.MissingPackageRoots) {
	# TODO: figure out how to dynamically get the name of Edit-PogRootList including current command prefix
	Write-Warning ("Could not find package root '$_'. Create the directory, or remove it" `
			+ " from the package root list using the 'Edit-PogRootList' command.")
}