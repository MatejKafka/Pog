. $PSScriptRoot\lib\header.ps1

if (Test-Path Env:POG_REMOTE_REPOSITORY_URL) {
	$Initiated = [Pog.InternalState]::InitRepository({[Pog.RemoteRepository]::new($env:POG_REMOTE_REPOSITORY_URL)})
	if ($Initiated) {
		Write-Information "Using remote repository: $([Pog.InternalState]::Repository.Url)"
	}
}

$REPOSITORY = [Pog.InternalState]::Repository
$GENERATOR_REPOSITORY = [Pog.InternalState]::GeneratorRepository
$PACKAGE_ROOTS = [Pog.InternalState]::ImportedPackageManager
Export-ModuleMember -Variable REPOSITORY, GENERATOR_REPOSITORY, PACKAGE_ROOTS

# warn about missing package roots
foreach ($r in [Pog.InternalState]::PathConfig.PackageRoots.MissingPackageRoots) {
	Write-Warning ("Could not find package root '$r'. Create the directory, or remove it" `
			+ " from the package root list using the 'Edit-PogRootList' command.")
}