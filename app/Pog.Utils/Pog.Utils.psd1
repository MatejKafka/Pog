@{
	ModuleVersion = '0.12.0'
	GUID = 'f6961fe2-dea0-4780-82e8-1376d14ffd02'
	Author = 'Matej Kafka'
	CompatiblePSEditions = @('Desktop', 'Core')

	Description = @'
A companion module to Pog that exposes some of the internal cmdlets from the package environment
that might be useful for day-to-day interactive use and function independently from Pog.

This module is not exactly polished and might break sometimes.
'@

	NestedModules = @(
		"..\Pog\LoadPogDll.ps1"
		"..\Pog\container\Env_UpdateRepository.psm1"
	)

	VariablesToExport = @()

	AliasesToExport = @()

	CmdletsToExport = @(
		# Pog.dll
		"Expand-Archive7Zip"
		"Get-FileHash7Zip"
		"Invoke-FileDownload"

		# Env_UpdateRepository
		'Get-GithubRelease'
		'Get-GithubAsset'
	)

	FunctionsToExport = @(
		# Env_UpdateRepository
		'Get-NugetRelease'
		'Get-HashFromChecksumText'
		'Get-HashFromChecksumFile'
	)
}