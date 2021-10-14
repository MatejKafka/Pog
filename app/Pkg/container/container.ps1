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
	$PkgArguments,
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
Set-Variable -Scope Global -Option Constant -Name "_Pkg" -Value $InternalArguments
#$InternalArguments.Keys | % {
#	Set-Variable -Scope Global -Option Constant -Name ("Pkg_" + $_) -Value $InternalArguments[$_]
#}

# TOCTOU issue, check Invoke-Container for details
$Manifest = Invoke-Expression (Get-Content -Raw $ManifestPath)

# this probably cannot be a constant, as it would break internal behavior
Set-Variable -Name this -Value $Manifest

# cleanup variables
Remove-Variable ManifestPath
Remove-Variable InternalArguments
Remove-Variable PreferenceVariables

try {
	_pkg_main $Manifest[$ContainerType] $PkgArguments
} finally {
	# this is called even on `exit`, which is nice
	Write-Debug "Cleaning up..."
	_pkg_cleanup
	Write-Debug "Cleanup finished."
}