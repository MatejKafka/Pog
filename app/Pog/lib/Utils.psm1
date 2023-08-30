. $PSScriptRoot\header.ps1


Export function Resolve-VirtualPath {
	param([Parameter(Mandatory)]$Path)
	return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

Export function Invoke-DollarUnder {
	param([Parameter(Mandatory)][scriptblock]$Sb, $DollarUnder, [Parameter(ValueFromRemainingArguments)][object[]]$ExtraArgs)
	return $Sb.InvokeWithContext($null, [psvariable[]]@([psvariable]::new("_", $DollarUnder)), $ExtraArgs)
}

Export function Assert-Admin {
	param([string]$ErrorMessage = $null)

	$CurrentPrincipal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
	$IsAdmin = $CurrentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

	if (-not $IsAdmin) {
		if ($ErrorMessage) {
			throw $ErrorMessage
		}
		throw "This script requires administrator privilege to run correctly."
	}
}