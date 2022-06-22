# Requires -Version 7
using module .\Get-CallerPreference.psm1
. $PSScriptRoot\header.ps1

Export-ModuleMember -Function Get-CallerPreference


Export function Resolve-VirtualPath {
	param([Parameter(Mandatory)]$Path)
	return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
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

function Add-DynamicParam {
	param(
			[Parameter(Mandatory)]
			[System.Management.Automation.RuntimeDefinedParameter]
		$RuntimeParameter,
			[System.Management.Automation.RuntimeDefinedParameterDictionary]
		$ParameterDictionary
	)

	# create the dictionary (should contain all created dynamic params, returned from dynamicparam block)
	if ($null -eq $ParameterDictionary) {
		$ParameterDictionary = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()
	}
	$ParameterDictionary.Add($RuntimeParameter.Name, $RuntimeParameter)
	return $ParameterDictionary
}

Export function New-DynamicSwitchParam {
	param(
			[Parameter(Mandatory)]
			[string]
		$ParameterName,
			[System.Management.Automation.RuntimeDefinedParameterDictionary]
		$ParameterDictionary
	)

	# create a dummy collection of attributes
	$AttributeCollection = New-Object System.Collections.ObjectModel.Collection[System.Attribute]
	# add dummy parameter to fix the "cannot be specified in parameter set '__AllParameterSets'." error
	$AttributeCollection.Add([System.Management.Automation.ParameterAttribute]::new())

	# create and return the dynamic parameter
	$RuntimeParameter = New-Object System.Management.Automation.RuntimeDefinedParameter(
			$ParameterName, [switch], $AttributeCollection)
	return Add-DynamicParam $RuntimeParameter $ParameterDictionary
}

Export function New-DynamicParam {
	param(
			[Parameter(Mandatory)]
			[string]
		$ParameterName,
			[Parameter(Mandatory)]
			[int]
		$ParameterPosition,
			[Parameter(Mandatory)]
			[ScriptBlock]
		$ParamValueGenerator,
			[System.Management.Automation.RuntimeDefinedParameterDictionary]
		$ParameterDictionary
	)

	# Create the collection of attributes
	$AttributeCollection = New-Object System.Collections.ObjectModel.Collection[System.Attribute]

	# Create and set the parameters' attributes
	$ParameterAttribute = New-Object System.Management.Automation.ParameterAttribute
	$ParameterAttribute.Mandatory = $true
	$ParameterAttribute.Position = $ParameterPosition

	# Add the attributes to the attributes collection
	$AttributeCollection.Add($ParameterAttribute)

	# Generate and set the ValidateSet
	$arrSet = & $ParamValueGenerator
	$ValidateSetAttribute = New-Object System.Management.Automation.ValidateSetAttribute($arrSet)

	# Add the ValidateSet to the attributes collection
	$AttributeCollection.Add($ValidateSetAttribute)

	# Create and return the dynamic parameter
	$RuntimeParameter = New-Object System.Management.Automation.RuntimeDefinedParameter(
			$ParameterName, [string], $AttributeCollection)

	return Add-DynamicParam $RuntimeParameter $ParameterDictionary
}

# source: https://gist.github.com/dbroeglin/c6ce3e4639979fa250cf
Export function Compare-Hashtable {
	<#
	.SYNOPSIS
		Compare two Hashtable and returns an array of differences.
	.DESCRIPTION
		The Compare-Hashtable function computes differences between two Hashtables. Results are returned as
		an array of objects with the properties: "key" (the name of the key that caused a difference),
		"side" (one of "<=", "!=" or "=>"), "lvalue" an "rvalue" (resp. the left and right value
		associated with the key).
	.PARAMETER left
		The left hand side Hashtable to compare.
	.PARAMETER right
		The right hand side Hashtable to compare.
	.EXAMPLE
		# Returns a difference for ("3 <="), c (3 "!=" 4) and e ("=>" 5).
		Compare-Hashtable @{ a = 1; b = 2; c = 3 } @{ b = 2; c = 4; e = 5}
	.EXAMPLE
		# Returns a difference for a ("3 <="), c (3 "!=" 4), e ("=>" 5) and g (6 "<=").
		$left = @{ a = 1; b = 2; c = 3; f = $Null; g = 6 }
		$right = @{ b = 2; c = 4; e = 5; f = $Null; g = $Null }
		Compare-Hashtable $left $right
	#>
	[CmdletBinding()]
	param (
		[Parameter(Mandatory = $true)]
		[Hashtable]$Left,

		[Parameter(Mandatory = $true)]
		[Hashtable]$Right
	)

	function New-Result($Key, $LValue, $Side, $RValue) {
		New-Object -Type PSObject -Property @{
			Key = $Key
			LeftValue = $LValue
			RightValue = $RValue
			Side = $Side
		}
	}

	[Object[]]$Results = $Left.Keys | % {
		if ($Left.ContainsKey($_) -and !$Right.ContainsKey($_)) {
			New-Result $_ $Left[$_] "<=" $Null
		} else {
			$LValue, $RValue = $Left[$_], $Right[$_]
			if ($LValue -ne $RValue) {
				New-Result $_ $LValue "!=" $RValue
			}
		}
	}
	$Results += $Right.Keys | % {
		if (!$Left.ContainsKey($_) -and $Right.ContainsKey($_)) {
			New-Result $_ $Null "=>" $Right[$_]
		}
	}
	return $Results
}
