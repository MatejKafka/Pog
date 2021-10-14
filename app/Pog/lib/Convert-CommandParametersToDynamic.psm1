# Requires -Version 7
# source: https://social.technet.microsoft.com/Forums/en-US/21fb4dd5-360d-4c76-8afc-1ad0bd3ff71a/reuse-function-parameters
# with slight modifications (added -NamePrefix parameter)

$CommonParameterNames = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([type] [System.Management.Automation.Internal.CommonParameters]) `
	| Get-Member -MemberType Properties `
	| Select-Object -ExpandProperty Name

# Param attributes will be copied later. You basically have to create a blank attrib, then change the
# properties. Knowing the writable ones up front helps:
$WritableParamAttributePropertyNames = New-Object System.Management.Automation.ParameterAttribute `
	| Get-Member -MemberType Property `
	| Where-Object { $_.Definition -match "{.*set;.*}$" } `
	| Select-Object -ExpandProperty Name


function Convert-CommandParametersToDynamic {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$true, ValueFromPipeline=$true)]
		$ParameterDictionary,
		[string]
		$NamePrefix = "",
		[switch] $AllowAliases,
		[switch] $AllowPositionAttributes
	)

	begin {
		$__WritableParamAttributePropertyNames = if (-not $AllowPositionAttributes) {
			# If you don't want to allow Position attributes, tell the function that it's not a writable attribute
			$script:WritableParamAttributePropertyNames | where { $_ -ne "Position" }
		} else {
			$script:WritableParamAttributePropertyNames
		}
	}

	process {
		# Convert to object array and get rid of Common params:
		$Parameters = $ParameterDictionary.GetEnumerator() | Where-Object { $CommonParameterNames -notcontains $_.Key }

		# Create the dictionary that this scriptblock will return:
		$DynParamDictionary = New-Object System.Management.Automation.RuntimeDefinedParameterDictionary

		foreach ($Parameter in $Parameters) {
			$AttribColl = New-Object System.Collections.ObjectModel.Collection[System.Attribute]

			$Parameter.Value.Attributes | ForEach-Object {
				$CurrentAttribute = $_
				$AttributeTypeName = $_.TypeId.FullName

				switch -wildcard ($AttributeTypeName) {
					System.Management.Automation.ArgumentTypeConverterAttribute {
						return  # So blank param doesn't get added
					}

					System.Management.Automation.AliasAttribute {
						if ($AllowAliases) {
							if ([string]::IsNullOrEmpty($NamePrefix)) {
								$AttribColl.Add($CurrentAttribute)
							} else {
								# add NamePrefix to all aliases
								$Prefixed = $CurrentAttribute.AliasNames | % {$NamePrefix + $_}
								$Attr = New-Object System.Management.Automation.AliasAttribute $Prefixed
								$AttribColl.Add($Attr)
							}
						}
					}

					System.Management.Automation.Validate*Attribute {
						$NewParamAttribute = $CurrentAttribute
						$AttribColl.Add($NewParamAttribute)
					}

					System.Management.Automation.ParameterAttribute {
						$NewParamAttribute = New-Object System.Management.Automation.ParameterAttribute

						foreach ($PropName in $__WritableParamAttributePropertyNames) {
							if ($NewParamAttribute.$PropName -ne $CurrentAttribute.$PropName) {
								# nulls cause an error if you assign them to some of the properties
								$NewParamAttribute.$PropName = $CurrentAttribute.$PropName
							}
						}

						if ($RemoveMandatoryAttribute) {
							$NewParamAttribute.Mandatory = $false
						}
						$NewParamAttribute.ParameterSetName = $CurrentAttribute.ParameterSetName

						$AttribColl.Add($NewParamAttribute)
					}

					default {
						Write-Warning "'Convert-CommandParametersToDynamic' doesn't handle dynamic " +`
								"parameter copying for '${AttributeTypeName}'.`n" +`
								"If this is something that you think you need as a package author, " +`
								"open a new issue and we'll see what we can do."
						return
					}
				}
			}

			$ParameterType = $Parameter.Value.ParameterType

			$DynamicParameter = New-Object System.Management.Automation.RuntimeDefinedParameter(
				($NamePrefix + $Parameter.Key),
				$ParameterType,
				$AttribColl
			)
			$DynParamDictionary.Add($NamePrefix + $Parameter.Key, $DynamicParameter)
		}

		# Return the dynamic parameters
		return $DynParamDictionary
	}
}