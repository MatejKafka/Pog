param(
		[Parameter(Mandatory)]
		[string]
	$ScriptBlockStr,
		[Parameter(Mandatory)]
		[Hashtable]
	$InternalArguments,
		[Parameter(Mandatory)]
		[Hashtable]
	$PkgArguments
)

$ScriptBlock = [ScriptBlock]::Create($ScriptBlockStr)

# create global constants from internal arguments
$InternalArguments.Keys | % {
	Set-Variable -Scope Global -Option Constant `
			-Name ("Pkg_" + $_) -Value $InternalArguments[$_]
}

# cleanup variables
Remove-Variable ScriptBlockStr
Remove-Variable InternalArguments

# FIXME: double call, due to a supposed Import-PowerShellDataFile bug
& (& $ScriptBlock) @PkgArguments
