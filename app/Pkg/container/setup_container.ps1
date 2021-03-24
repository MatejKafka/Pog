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