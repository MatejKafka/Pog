. $PSScriptRoot\..\header.ps1

Import-Module $PSScriptRoot"\..\Utils"


function Update-EnvVar {
	param(
			[Parameter(Mandatory)]
			[string]
		$VarName
	)
	
	$Machine = [Environment]::GetEnvironmentVariable($VarName, [EnvironmentVariableTarget]::Machine)
	$User = [Environment]::GetEnvironmentVariable($VarName, [EnvironmentVariableTarget]::User)
	
	$Value = if ($null -eq $Machine -or $null -eq $User) {
		[string]($Machine + $User)
	} else {
		$Machine + [IO.Path]::PathSeparator + $User
	}
	[Environment]::SetEnvironmentVariable($VarName, $Value)
}

function _Set-SystemEnvVar {
	param(
			[Parameter(Mandatory)]
			[string]
		$VarName,		
			[Parameter(Mandatory)]
			[string]
		$Value
	)
	
	[Environment]::SetEnvironmentVariable($VarName, $Value, [EnvironmentVariableTarget]::Machine)
	Update-EnvVar $VarName
}

Export function Set-SystemEnvVar {
	param(
			[Parameter(Mandatory)]
			[string]
		$VarName,		
			[Parameter(Mandatory)]
			[string]
		$Value
	)
	
	$OldValue = [Environment]::GetEnvironmentVariable($VarName, [EnvironmentVariableTarget]::Machine)
	if ($OldValue -eq $Value) {
		return "env:$VarName already set to '$Value'."
	}
	
	Assert-Admin "Cannot write system environment variable $VarName without administrator privileges."
	Write-Warning "Set environment variable env:$VarName to '$Value'."
	_Set-SystemEnvVar $VarName $Value
}

<#
.SYNOPSIS
	Appends item to a given system-scope environment variable.
.PARAMETER VarName
	Name of modified environment variable.
.PARAMETER Value
	Value to add.
#>
Export function Add-SystemEnvVar {
	param(
			[Parameter(Mandatory)]
			[string]
		$VarName,		
			[Parameter(Mandatory)]
			[string]
		$Value,
			[switch]
		$Prepend
	)

	$OldValue = [Environment]::GetEnvironmentVariable($VarName, [EnvironmentVariableTarget]::Machine)
	if ($OldValue.Split([IO.Path]::PathSeparator).Contains($Value)) {
		return "Value '$Value' already inserted in env:$VarName."
	}
	
	Assert-Admin "Cannot add '$Value' to $VarName without administrator privileges."
	Write-Warning "Adding '$Value' to env:$VarName."
	
	if ($Prepend) {
		_Set-SystemEnvVar $VarName ($Value + [IO.Path]::PathSeparator + $OldValue)
	} else {
		_Set-SystemEnvVar $VarName ($OldValue + [IO.Path]::PathSeparator + $Value)
	}
	
	return "Inserted '$Value' into env:$VarName."
}

<#
.SYNOPSIS
	Adds folder to system PATH env variable.
.PARAMETER Folder
	Folder to add.
#>
Export function Add-SystemEnvPath {
	param(
			[ValidateScript({Test-Path -PathType Container $_})]
			[Parameter(Mandatory)]
			[string]
		$Folder,
			[switch]
		$Prepend
	)

	$AbsPath = Resolve-Path $Folder
	Add-SystemEnvVar "Path" $AbsPath -Prepend:$Prepend
}