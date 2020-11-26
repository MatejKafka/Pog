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


# source: https://gallery.technet.microsoft.com/scriptcenter/Inherit-Preference-82343b9d
Export function Get-CallerPreference {
	<#
	.Synopsis
	   Fetches "Preference" variable values from the caller's scope.
	.DESCRIPTION
	   Script module functions do not automatically inherit their caller's variables, but they can be
	   obtained through the $PSCmdlet variable in Advanced Functions.  This function is a helper function
	   for any script module Advanced Function; by passing in the values of $ExecutionContext.SessionState
	   and $PSCmdlet, Get-CallerPreference will set the caller's preference variables locally.
	.PARAMETER Cmdlet
	   The $PSCmdlet object from a script module Advanced Function.
	.PARAMETER SessionState
	   The $ExecutionContext.SessionState object from a script module Advanced Function.  This is how the
	   Get-CallerPreference function sets variables in its callers' scope, even if that caller is in a different
	   script module.
	.PARAMETER Name
	   Optional array of parameter names to retrieve from the caller's scope.  Default is to retrieve all
	   Preference variables as defined in the about_Preference_Variables help file (as of PowerShell 4.0)
	   This parameter may also specify names of variables that are not in the about_Preference_Variables
	   help file, and the function will retrieve and set those as well.
	.EXAMPLE
	   Get-CallerPreference -Cmdlet $PSCmdlet -SessionState $ExecutionContext.SessionState

	   Imports the default PowerShell preference variables from the caller into the local scope.
	.EXAMPLE
	   Get-CallerPreference -Cmdlet $PSCmdlet -SessionState $ExecutionContext.SessionState -Name 'ErrorActionPreference','SomeOtherVariable'

	   Imports only the ErrorActionPreference and SomeOtherVariable variables into the local scope.
	.EXAMPLE
	   'ErrorActionPreference','SomeOtherVariable' | Get-CallerPreference -Cmdlet $PSCmdlet -SessionState $ExecutionContext.SessionState

	   Same as Example 2, but sends variable names to the Name parameter via pipeline input.
	.INPUTS
	   String
	.OUTPUTS
	   None.  This function does not produce pipeline output.
	.LINK
	   about_Preference_Variables
	#>

	[CmdletBinding(DefaultParameterSetName = 'AllVariables')]
	param (
			[Parameter(Mandatory = $true)]
			[ValidateScript({ $_.GetType().FullName -eq 'System.Management.Automation.PSScriptCmdlet' })]
		$Cmdlet,
			[Parameter(Mandatory = $true)]
			[System.Management.Automation.SessionState]
		$SessionState,
			[Parameter(ParameterSetName = 'Filtered', ValueFromPipeline = $true)]
			[string[]]
		$Name
	)

	begin {
		$filterHash = @{}
	}
	
	process {
		if ($null -ne $Name) {
			foreach ($string in $Name) {
				$filterHash[$string] = $true
			}
		}
	}

	end {	
		# List of preference variables taken from the about_Preference_Variables
		#  help file in PowerShell version 7.1
		$vars = @{
			'Transcript' = $null
			'ErrorView' = $null
			'FormatEnumerationLimit' = $null
			'LogCommandHealthEvent' = $null
			'LogCommandLifecycleEvent' = $null
			'LogEngineHealthEvent' = $null
			'LogEngineLifecycleEvent' = $null
			'LogProviderHealthEvent' = $null
			'LogProviderLifecycleEvent' = $null
			'MaximumAliasCount' = $null
			'MaximumDriveCount' = $null
			'MaximumErrorCount' = $null
			'MaximumFunctionCount' = $null
			'MaximumHistoryCount' = $null
			'MaximumVariableCount' = $null
			'OFS' = $null
			'OutputEncoding' = $null
			'ProgressPreference' = $null
			'PSDefaultParameterValues' = $null
			'PSEmailServer' = $null
			'PSModuleAutoLoadingPreference' = $null
			'PSSessionApplicationName' = $null
			'PSSessionConfigurationName' = $null
			'PSSessionOption' = $null

			'ErrorActionPreference' = 'ErrorAction'
			'DebugPreference' = 'Debug'
			'ConfirmPreference' = 'Confirm'
			'WhatIfPreference' = 'WhatIf'
			'VerbosePreference' = 'Verbose'
			'WarningPreference' = 'WarningAction'
			'InformationPreference' = 'InformationAction'
		}


		foreach ($entry in $vars.GetEnumerator()) {
			if (([string]::IsNullOrEmpty($entry.Value) -or -not $Cmdlet.MyInvocation.BoundParameters.ContainsKey($entry.Value)) -and
				($PSCmdlet.ParameterSetName -eq 'AllVariables' -or $filterHash.ContainsKey($entry.Name))
			) {
				$variable = $Cmdlet.SessionState.PSVariable.Get($entry.Key)
				
				if ($null -ne $variable) {
					if ($SessionState -eq $ExecutionContext.SessionState) {
						Set-Variable -Scope 1 -Name $variable.Name -Value $variable.Value -Force -Confirm:$false -WhatIf:$false
					} else {
						$SessionState.PSVariable.Set($variable.Name, $variable.Value)
					}
				}
			}
		}

		if ($PSCmdlet.ParameterSetName -eq 'Filtered') {
			foreach ($varName in $filterHash.Keys) {
				if (-not $vars.ContainsKey($varName)) {
					$variable = $Cmdlet.SessionState.PSVariable.Get($varName)
				
					if ($null -ne $variable) {
						if ($SessionState -eq $ExecutionContext.SessionState) {
							Set-Variable -Scope 1 -Name $variable.Name -Value $variable.Value -Force -Confirm:$false -WhatIf:$false
						} else {
							$SessionState.PSVariable.Set($variable.Name, $variable.Value)
						}
					}
				}
			}
		}
	}
}