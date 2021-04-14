. $PSScriptRoot\header.ps1

Import-Module $PSScriptRoot\lib\Get-CallerPreference

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
		$ParameterDictionary = New-Object System.Management.Automation.RuntimeDefinedParameterDictionary
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