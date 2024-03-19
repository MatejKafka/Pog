. $PSScriptRoot\header.ps1


Export function Resolve-VirtualPath {
	param([Parameter(Mandatory)]$Path)
	return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

Export function Invoke-DollarUnder {
	param([Parameter(Mandatory)][scriptblock]$Sb, $DollarUnder, [Parameter(ValueFromRemainingArguments)][object[]]$ExtraArgs)
	return $Sb.InvokeWithContext($null, [psvariable[]]@([psvariable]::new("_", $DollarUnder)), $ExtraArgs)
}
