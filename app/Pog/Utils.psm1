. $PSScriptRoot\header.ps1


function Resolve-VirtualPath {
	### .SYNOPSIS
	### Resolve path to an absolute path without expanding wildcards or checking if $Path exists.
	param([Parameter(Mandatory)]$Path)
	return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

# this must NOT be an advanced funtion, otherwise we lose error message position from the manifest scriptblock
function Invoke-DollarUnder {
	### .SYNOPSIS
	### Invoke $Sb with $DollarUnder set as $_ and $PSItem (mirrors what e.g. `Foreach-Object` does).
	param($Sb, $DollarUnder)

	try {
		return $Sb.InvokeWithContext($null, [psvariable[]]@([psvariable]::new("_", $DollarUnder)), $Args)
	} catch [System.Management.Automation.MethodInvocationException] {
		# rethrow inner exception
		throw $_.Exception.InnerException
	}
}

function New-ContainerModule {
	### .SYNOPSIS
	### Returns an in-memory PowerShell module with pre-configured strict mode and $ErrorActionPreference.
	###
	### .DESCRIPTION
	### The returned module should be used to invoke all package manifest entry points, otherwise they do not run
	### in strict mode and they see the internals of the module that invoked them.
	###
	### By default, when function A from ModuleA is invoking function B in ModuleB and passes a scriptblock callback
	### that is invoked by B, the scriptblock keeps its association with ModuleA and does not see internals of ModuleB.
	### However, the manifest scriptblocks are created from C# and are not associated with a module, which for some
	### reason causes the scriptblock to see the internals of whatever module that is invoking it. To avoid that,
	### we create an in-memory module and bind the scriptblock to it.
	###
	### An alternative option that might be worth exploring is seeing what happens when you invoke the unbound
	### scriptblock in global scope using the Pog.Container [PowerShell] instance, I suspect it might achieve
	### a similar result.
	[OutputType([psmoduleinfo])]
	param()

	# this is roughly equivalent to [psmoduleinfo]::new({})
	return New-Module -Name PogContainer {
		Set-StrictMode -Version 3
		$ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop
	}
}

Export-ModuleMember -Function Resolve-VirtualPath, Invoke-DollarUnder, New-ContainerModule