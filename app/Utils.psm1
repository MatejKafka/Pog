. $PSScriptRoot\header.ps1


Export function Resolve-VirtualPath {
	param([Parameter(Mandatory)]$Path)
	return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}


Export function Get-RelativePath {
	param(
			[Parameter(Mandatory)]
		$From,
			[Parameter(Mandatory)]
		$To
	)
	
	$From = Resolve-Path $From
	if (Test-Path -Type Leaf $From) {
		$From = Resolve-Path (Split-Path $From)
	}
	# append \, otherwise the resulting relative path has needless ..\dir
	#  (e.g path from C:\dir to C:\dir would be ..\dir)
	$To = [string](Resolve-Path $To) + [System.IO.Path]::DirectorySeparatorChar
	
	try {
		Push-Location $From
		return Resolve-Path -Relative $To
	} finally {
		Pop-Location
	}
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
			
	# Create the dictionary
	if ($null -eq $ParameterDictionary) {
		$ParameterDictionary = New-Object System.Management.Automation.RuntimeDefinedParameterDictionary
	}
	$ParameterDictionary.Add($ParameterName, $RuntimeParameter)
	return $ParameterDictionary
}
