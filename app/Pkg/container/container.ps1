param(
		[Parameter(Mandatory)]
		[string]
	$ScriptBlockStr,
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

$PreferenceVariables.GetEnumerator() | % {
	Set-Variable -Name $_.Name -Value $_.Value
}

$ScriptBlock = [ScriptBlock]::Create($ScriptBlockStr)

# create global constants from internal arguments
$InternalArguments.Keys | % {
	Set-Variable -Scope Global -Option Constant `
			-Name ("Pkg_" + $_) -Value $InternalArguments[$_]
}
# this probably cannot be a constant, as it would break internal behavior
Set-Variable -Name this -Value $Pkg_Manifest

# cleanup variables
Remove-Variable ScriptBlockStr
Remove-Variable InternalArguments
Remove-Variable PreferenceVariables

# FIXME: double call, due to a supposed Import-PowerShellDataFile bug
. (. $ScriptBlock) @PkgArguments
