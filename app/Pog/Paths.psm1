. $PSScriptRoot\lib\header.ps1

$REPOSITORY = [Pog.InternalState]::Repository
$GENERATOR_REPOSITORY = [Pog.InternalState]::GeneratorRepository
$PACKAGE_ROOTS = [Pog.InternalState]::ImportedPackageManager
Export-ModuleMember -Variable REPOSITORY, GENERATOR_REPOSITORY, PACKAGE_ROOTS

# warn about missing package roots
foreach ($r in [Pog.InternalState]::PathConfig.PackageRoots.MissingPackageRoots) {
	Write-Warning ("Could not find package root '$r'. Create the directory, or remove it" `
			+ " from the package root list using the 'Edit-PogRootList' command.")
}