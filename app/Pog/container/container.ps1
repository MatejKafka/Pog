# Requires -Version 7
param(
		[Parameter(Mandatory)]
		[string]
	$ContainerType,
		[Parameter(Mandatory)]
		[string]
	$ManifestPath,
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

Set-StrictMode -Version Latest

# block automatic module loading to isolate the configuration script from system packages
# this allows for a more consistent environment between different machines
$PSModuleAutoLoadingPreference = "None"
$ErrorActionPreference = "Stop"

# these two imports contain basic stuff needed for printing output, errors, FS traversal,...
Import-Module Microsoft.PowerShell.Utility
Import-Module Microsoft.PowerShell.Management

# setup environment for package manifest script
# each environment module must provide a `__main` and `__cleanup` functions
switch ($ContainerType) {
	Enable {Import-Module $PSScriptRoot\Enable\Env_Enable}
	Install {Import-Module $PSScriptRoot\Install\Env_Install}
	default {throw "Unknown container environment type: " + $_}
}

# override Import-Module to hide the default verbose prints when -Verbose is set for the container environment
$_OrigImport = Get-Command Import-Module
function global:Import-Module {
	# $VerbosePreference is set globally in container.ps1, so we'd need to overwrite it, and then set it back,
	#  as it interacts weirdly with -Verbose:$false, which apparently doesn't work here for some reason;
	#  it seems as the cleanest solution to do `4>$null`, which just hides the Verbose stream alltogether
	& $_OrigImport @Args 4>$null
}


# copy preference variables from outside
$PreferenceVariables.GetEnumerator() | % {
	Set-Variable -Name $_.Name -Value $_.Value
}
# create global constant from internal arguments
# we use ReadOnly instead of Constant so that we can remove this variable
#  afterwards in case we're not running in a separate job/runspace
Set-Variable -Scope Global -Option ReadOnly -Name "_InternalArgs" -Value $InternalArguments

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
	# invoke the container module entry point, which invokes the manifest script itself
	__main $this[$ContainerType] $PackageArguments
} finally {
	# this is called even on `exit`, which is nice
	Write-Debug "Cleaning up..."
	__cleanup
	Write-Debug "Cleanup finished."

	# cleanup custom functions and variables – this is necessary when running the container without isolation
	# remove the overriden Import-Module function (defined above)
	Remove-Item Function:\Import-Module
	# remove the read-only _InternalArgs variable
	Remove-Variable -Force "_InternalArgs" -Scope Global
}
