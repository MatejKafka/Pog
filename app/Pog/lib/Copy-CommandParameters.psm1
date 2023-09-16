. $PSScriptRoot\header.ps1

# source: Pester (InModuleScope and Pester.Scoping) and https://gist.github.com/nohwnd/0f615f897b1f510beb08ce0cefe48342
function Set-ScriptBlockScope {
    [CmdletBinding()]
    param (
            [Parameter(Mandatory)]
            [scriptblock]
        $ScriptBlock,
            [Parameter(Mandatory)]
            [System.Management.Automation.SessionState]
        $SessionState
    )
    $flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
    $SessionStateInternal = $SessionState.GetType().GetProperty('Internal', $flags).GetValue($SessionState, $null)
    [scriptblock].GetProperty('SessionStateInternal', $flags).SetValue($ScriptBlock, $SessionStateInternal, $null)
}

$CommonParameterNames = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([type] [System.Management.Automation.Internal.CommonParameters]) `
    | Get-Member -MemberType Properties `
    | Select-Object -ExpandProperty Name

# Param attributes will be copied later. You basically have to create a blank attrib, then change the
# properties. Knowing the writable ones up front helps:
$WritableParamAttributePropertyNames = [System.Management.Automation.ParameterAttribute]::new() `
    | Get-Member -MemberType Property `
    | Where-Object { $_.Definition -match "{.*set;.*}$" } `
    | Select-Object -ExpandProperty Name


class DynamicCommandParameters : System.Management.Automation.RuntimeDefinedParameterDictionary {
    hidden [string]$_NamePrefix

    DynamicCommandParameters([string]$NamePrefix) {
        $this._NamePrefix = $NamePrefix
    }

    [Hashtable] Extract([Hashtable]$_PSBoundParameters) {
        $Extracted = @{}
        foreach ($ParamName in $this.Keys) {
            if ($_PSBoundParameters.ContainsKey($ParamName)) {
                $Extracted[$ParamName.Substring($this._NamePrefix.Length)] = $_PSBoundParameters[$ParamName]
            }
        }
        return $Extracted
    }
}

# source: https://social.technet.microsoft.com/Forums/en-US/21fb4dd5-360d-4c76-8afc-1ad0bd3ff71a/reuse-function-parameters
# I made some modifications and extensions.
Export function Copy-CommandParameters {
    [CmdletBinding(PositionalBinding = $false, DefaultParameterSetName = "CommandInfo")]
    [OutputType([DynamicCommandParameters])]
    param(
            [Parameter(Mandatory, Position = 0, ValueFromPipeline, ParameterSetName = "CommandInfo")]
            [System.Management.Automation.CommandInfo]
        $CommandInfo,
            [Parameter(Mandatory, Position = 0, ValueFromPipeline, ParameterSetName = "ScriptBlock")]
            [scriptblock]
        $ScriptBlock,
            [string]
        $NamePrefix = "",
            [switch]
        $NoAlias,
            [switch]
        $NoPositionAttribute,
            [switch]
        $NoMandatoryAttribute
    )

    begin {
        $__WritableParamAttributePropertyNames = if ($NoPositionAttribute) {
            # remove Position from the list of allowed properties for Parameter()
            $script:WritableParamAttributePropertyNames | ? {$_ -ne "Position"}
        } else {
            $script:WritableParamAttributePropertyNames
        }
    }

    process {
        if ($ScriptBlock) {
            # we cannot extract parameters directly from a scriptblock
            # instead, we'll turn it into a temporary function and use Get-Command to read the parameters
            # see https://github.com/PowerShell/PowerShell/issues/13774

            # this is only set in local scope, no need to clean up
            $function:TmpFn = $ScriptBlock
            $CommandInfo = Get-Command -Type Function TmpFn
        } else {
            while ($CommandInfo.CommandType -eq "Alias") {
                # resolve alias
                $CommandInfo = $CommandInfo.ResolvedCommand
            }

            if ($null -eq $CommandInfo.Parameters) {
                throw "Cannot copy parameters from command '$($CommandInfo.Name)', no parameters are accessible (this may happen e.g. for native executables)."
            }
        }

        $ParameterDictionary = $CommandInfo.Parameters
        # module context is used to set correct scope for attributes taking a scriptblock like ValidateScript and ArgumentCompleter
        # this is only really relevant for functions (cmdlets shouldn't have problems with scope, attributes in script param() block
        #  cannot refer to things inside the script, native executables don't have visible parameters)
        $ModuleContext = if ($CommandInfo | Get-Member ScriptBlock) {
            $CommandInfo.ScriptBlock.Module
        } else {$null}

        # Convert to object array and get rid of Common params:
        $Parameters = $ParameterDictionary.GetEnumerator() | Where-Object { $CommonParameterNames -notcontains $_.Key }
        $ParameterNameSet = [System.Collections.Generic.HashSet[string]]($Parameters | % Key)

        # Create the dictionary that this scriptblock will return:
        $DynParamDictionary = [DynamicCommandParameters]::new($NamePrefix)

        foreach ($Parameter in $Parameters) {
            $AttribColl = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()

            $Parameter.Value.Attributes | ForEach-Object {
                $CurrentAttribute = $_
                $AttributeTypeName = $_.TypeId.FullName

                switch -wildcard ($AttributeTypeName) {
                    System.Management.Automation.ArgumentTypeConverterAttribute {
                        # parameter type is set directly on the $Parameter object, this attribute seems useless
                        return  # return so blank param doesn't get added
                    }

                    System.Management.Automation.AliasAttribute {
                        if ($NoAlias) {
                            break
                        }
                        if ([string]::IsNullOrEmpty($NamePrefix)) {
                            $AttribColl.Add($CurrentAttribute)
                        } else {
                            # add NamePrefix to all aliases
                            $Prefixed = $CurrentAttribute.AliasNames | % {$NamePrefix + $_}
                            $Attr = [System.Management.Automation.AliasAttribute]::new($Prefixed)
                            $AttribColl.Add($Attr)
                        }
                        break
                    }

                    System.Management.Automation.ArgumentCompleterAttribute {
                        if (-not $NamePrefix) {
                            # just copy
                            $AttribColl.Add($CurrentAttribute)
                            break
                        }
                        # the completer will often refer to values of other already bound parameters; however, when -NamePrefix is set,
                        #  the names of the real parameters will be different, so we'll have to translate
                        $AttribColl.Add([ArgumentCompleter]::new({
                            [CmdletBinding()]
                            param($CmdName, $ParamName, $WordToComplete, $Ast, $BoundParameters)

                            $RenamedParameters = @{}
                            foreach ($e in $BoundParameters.GetEnumerator()) {
                                if ($e.Key.StartsWith($NamePrefix)) {
                                    $OrigName = $e.Key.Substring($NamePrefix.Length)
                                    if ($OrigName -in $ParameterNameSet) {
                                        $RenamedParameters[$OrigName] = $e.Value
                                    }
                                }
                            }

                            if ($null -ne $CurrentAttribute.ScriptBlock) {
                                return & $CurrentAttribute.ScriptBlock $CmdName $ParamName $WordToComplete $Ast $RenamedParameters
                            } else {
                                return $CurrentAttribute.Type::new().CompleteArgument($CmdName, $ParamName, $WordToComplete, $Ast, $RenamedParameters)
                            }
                        }.GetNewClosure()))
                        break
                    }

                    System.Management.Automation.ValidateScriptAttribute {
                        if (-not $ModuleContext) {
                            # just copy
                            $AttribColl.Add($CurrentAttribute)
                        } else {
                            $Sb = $CurrentAttribute.ScriptBlock
                            Set-ScriptBlockScope $Sb -SessionState $ModuleContext.SessionState
                            $AttribColl.Add([ValidateScript]::new($Sb))
                        }
                        break
                    }

                    System.Management.Automation.Validate*Attribute {
                        # just copy
                        $AttribColl.Add($CurrentAttribute)
                        break
                    }

                    System.Management.Automation.AllowNullAttribute {
                        # just copy
                        $AttribColl.Add($CurrentAttribute)
                        break
                    }

                    System.Management.Automation.AllowEmptyStringAttribute {
                        # just copy
                        $AttribColl.Add($CurrentAttribute)
                        break
                    }

                    System.Management.Automation.ParameterAttribute {
                        $NewParamAttribute = [System.Management.Automation.ParameterAttribute]::new()

                        foreach ($PropName in $__WritableParamAttributePropertyNames) {
                            if ($NewParamAttribute.$PropName -ne $CurrentAttribute.$PropName) {
                                # nulls cause an error if you assign them to some of the properties
                                $NewParamAttribute.$PropName = $CurrentAttribute.$PropName
                            }
                        }

                        if ($NoMandatoryAttribute) {
                            $NewParamAttribute.Mandatory = $false
                        }
                        $NewParamAttribute.ParameterSetName = $CurrentAttribute.ParameterSetName

                        $AttribColl.Add($NewParamAttribute)
                        break
                    }

                    System.Runtime.CompilerServices.* {
                        return # ignore, this is e.g. [Nullable], which is generated for nullable fields (e.g. `string?`) automatically
                    }

                    default {
                        Write-Warning ("'Convert-CommandParametersToDynamic' doesn't handle the dynamic parameter attribute " +`
                                "'${AttributeTypeName}', defined for parameter '$($Parameter.Key)' of the manifest block.`n" +`
                                "If this is something that you think you need as a package author, " +`
                                "open a new issue and we'll see what we can do.")
                        return
                    }
                }
            }

            $ParameterType = $Parameter.Value.ParameterType

            $DynamicParameter = [System.Management.Automation.RuntimeDefinedParameter]::new(
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
