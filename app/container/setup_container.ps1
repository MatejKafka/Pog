Set-StrictMode -Version Latest

# block automatic module loading to isolate the configuration script from system packages
# this allows more consistent environment between different machines
$PSModuleAutoLoadingPreference = "None"
$ErrorActionPreference = "Stop"

# these two imports contain basic stuff needed for printing output, errors, FS traversal,...
Import-Module Microsoft.PowerShell.Utility
Import-Module Microsoft.PowerShell.Management

# setup environment for Pkg script
Import-Module $PSScriptRoot\Configure