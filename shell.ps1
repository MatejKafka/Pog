### This script sets up a PowerShell session so that Pog and any exported commands
### can be invoked without specifying full paths. Use this when you do not want to
### integrate Pog with your system by changing environment variables.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# add Pog bin dir to PATH
$env:PATH = "${PSScriptRoot}\data\package_bin;$env:PATH"

# add Pog to PSModulePath
$ModulePath = "${PSScriptRoot}\app\Pog"
if ($env:PSModulePath) {
    $ModulePath += "$;${env:PSModulePath}"
}
$env:PSModulePath = $ModulePath