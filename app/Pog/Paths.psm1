using module .\lib\Utils.psm1
. $PSScriptRoot\lib\header.ps1

if (Test-Path Env:POG_LOCAL_REPOSITORY_PATH) {
	$RepoPath = Resolve-VirtualPath $env:POG_LOCAL_REPOSITORY_PATH
	if (-not (Test-Path -Type Container $RepoPath)) {
		throw "Local repository path passed in `$env:POG_LOCAL_REPOSITORY_PATH does not exist or is not a directory: '$env:POG_LOCAL_REPOSITORY_PATH'"
	}

	$Initiated = [Pog.InternalState]::InitRepository({[Pog.LocalRepository]::new($RepoPath)})
	if ($Initiated) {
		Write-Information "Using local repository: $([Pog.InternalState]::Repository.Path)"
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