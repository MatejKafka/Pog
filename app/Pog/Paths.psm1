# Requires -Version 7
. $PSScriptRoot\lib\header.ps1

$PATH_CONFIG = [Pog.InternalState]::PathConfig
$REPOSITORY = [Pog.InternalState]::Repository
$GENERATOR_REPOSITORY = [Pog.InternalState]::GeneratorRepository
$PACKAGE_ROOTS = [Pog.InternalState]::ImportedPackageManager
$DOWNLOAD_CACHE = [Pog.InternalState]::DownloadCache
Export-ModuleMember -Variable PATH_CONFIG, REPOSITORY, GENERATOR_REPOSITORY, PACKAGE_ROOTS, DOWNLOAD_CACHE

# warn about missing package roots
foreach ($r in $PATH_CONFIG.PackageRoots.MissingPackageRoots) {
	# TODO: figure out how to dynamically get the name of Edit-PogRootList including current command prefix
	Write-Warning ("Could not find package root '$r'. Create the directory, or remove it" `
			+ " from the package root list using the 'Edit-PogRootList' command.")
}