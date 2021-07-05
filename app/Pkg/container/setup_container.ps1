param(
		[Parameter(Mandatory)]
		[string]
	$EnvType
)

Set-StrictMode -Version Latest

# block automatic module loading to isolate the configuration script from system packages
# this allows more consistent environment between different machines
$PSModuleAutoLoadingPreference = "None"
$ErrorActionPreference = "Stop"

# these two imports contain basic stuff needed for printing output, errors, FS traversal,...
Import-Module Microsoft.PowerShell.Utility
Import-Module Microsoft.PowerShell.Management

# setup environment for Pkg script
switch ($EnvType) {
	Enable {Import-Module $PSScriptRoot\Env_Enable}
	Install {Import-Module $PSScriptRoot\Env_Install}
	default {throw "Unknown Pkg container environment type: " + $_}
}

# override Import-Module to hide the default verbose prints when -Verbose is set for Pkg environment
$_OrigImport = Get-Command Import-Module
function global:Import-Module {
	# $VerbosePreference is set globally in container.ps1, so we'd need to overwrite it, and then set it back,
	#  as it interacts weirdly with -Verbose:$false, which apparently doesn't work here for some reason;
	#  it seems as the cleanest solution to do `4>$null`, which just hides the Verbose stream all-together
	& $_OrigImport @Args 4>$null
}