# Requires -Version 7
param(
		[Parameter(Mandatory)]
		[string]
	$ManifestPath,
		[Parameter(Mandatory)]
		[string]
	$ContainerType,
		[Parameter(Mandatory)]
		[Hashtable]
	$InternalArguments,
		[Parameter(Mandatory)]
		[Hashtable]
	$PackageArguments,
		[Parameter(Mandatory)]
		[Hashtable]
	$PreferenceVariables
)

# this is the main script running inside container, which sets up the environment and calls the manifest
# assumes setup_container.ps1 is called first
# we cannot trivially import it, because of https://github.com/PowerShell/PowerShell/issues/15096

# copy preference variables from outside
$PreferenceVariables.GetEnumerator() | % {
	Set-Variable -Name $_.Name -Value $_.Value
}
# create global constant from internal arguments
Set-Variable -Scope Global -Option Constant -Name "_InternalArgs" -Value $InternalArguments

# TOCTOU issue, check Invoke-Container for details
$Manifest = Invoke-Expression (Get-Content -Raw $ManifestPath)

# this probably cannot be a constant, as it would break internal behavior
Set-Variable -Name this -Value $Manifest
Set-Variable -Name _ -Value $Manifest
Set-Variable -Name ManifestRoot -Value (Resolve-Path -Relative (Split-Path $ManifestPath))

# cleanup variables
Remove-Variable Manifest
Remove-Variable ManifestPath
Remove-Variable InternalArguments
Remove-Variable PreferenceVariables

try {
	__main $this[$ContainerType] $PackageArguments
} finally {
	# this is called even on `exit`, which is nice
	Write-Debug "Cleaning up..."
	__cleanup
	Write-Debug "Cleanup finished."
}