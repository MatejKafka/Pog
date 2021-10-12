# Requires -Version 7
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
	Write-Debug "Updated PS environment variable '${VarName}' from system."
}


<#
.SYNOPSIS
	Sets environment variable.
.PARAMETER VarName
	Name of modified environment variable.
.PARAMETER Value
	Value to add.
.PARAMETER Systemwide
	If set, system environemnt variable will be set. Otherwise, user environment variable will be set.
#>
Export function Set-EnvVar {
	param(
			[Parameter(Mandatory)]
			[string]
		$VarName,		
			[Parameter(Mandatory)]
			[string]
		$Value,
			[switch]
		$Systemwide
	)
	
	$Target = if ($Systemwide) {[EnvironmentVariableTarget]::Machine}
		else {[EnvironmentVariableTarget]::User}
	
	$TargetReadable = if ($Systemwide) {"system-wide"} else {"for current user"}
	
	$OldValue = [Environment]::GetEnvironmentVariable($VarName, $Target)
	if ($OldValue -eq $Value) {
		Write-Verbose "'env:$VarName' already set to '$Value' $TargetReadable."
		# the var might be set in system, but our process might have the old/no value
		# this ensures that after this call, value of $env:VarName is up to date
		Update-EnvVar $VarName
		return
	}
	
	if ($Systemwide) {
		Assert-Admin "Cannot write system-wide environment variable 'env:$VarName' without administrator privileges."
	}
	
	Write-Warning "Setting environment variable 'env:$VarName' to '$Value' $TargetReadable..."
	[Environment]::SetEnvironmentVariable($VarName, $Value, $Target)
	Update-EnvVar $VarName
}

<#
.SYNOPSIS
	Appends item to a given system-scope environment variable.
.PARAMETER VarName
	Name of modified environment variable.
.PARAMETER Value
	Value to add.
.PARAMETER Systemwide
	If set, system environemnt variable will be set. Otherwise, user environment variable will be set.
#>
Export function Add-EnvVar {
	param(
			[Parameter(Mandatory)]
			[string]
		$VarName,		
			[Parameter(Mandatory)]
			[string]
		$Value,
			[switch]
		$Prepend,
			[switch]
		$Systemwide
	)
	
	$Target = if ($Systemwide) {[EnvironmentVariableTarget]::Machine}
		else {[EnvironmentVariableTarget]::User}
	
	$TargetReadable = if ($Systemwide) {"system-wide"} else {"for current user"}
	
	$OldValue = [Environment]::GetEnvironmentVariable($VarName, $Target)
	
	if ($null -eq $OldValue) {
		# variable not set yet
		Set-EnvVar $VarName $Value -Systemwide:$Systemwide
		return
	}
	
	if ($OldValue.Split([IO.Path]::PathSeparator).Contains($Value)) {
		Write-Verbose "Value '$Value' already present in 'env:$VarName' $TargetReadable."
		return
	}
	
	if ($Systemwide) {
		Assert-Admin "Cannot update system-wide environment variable 'env:$VarName' without administrator privileges."
	}
	
	Write-Warning "Adding '$Value' to 'env:$VarName' $TargetReadable..."
	
	$NewValue = if ($Prepend) {
		$Value + [IO.Path]::PathSeparator + $OldValue
	} else {
		$OldValue + [IO.Path]::PathSeparator + $Value
	}

	[Environment]::SetEnvironmentVariable($VarName, $NewValue, $Target)
	Update-EnvVar $VarName
	
	$Verb = if ($Prepend) {"Prepended"} else {"Appended"}
	Write-Information "$Verb '$Value' to 'env:$VarName'."
}

<#
.SYNOPSIS
	Adds directory to PATH env variable.
.PARAMETER Directory
	Directory path to add.
.PARAMETER Systemwide
	If set, system-wide environment variable will be set (requires elevation).
	Otherwise, user environment variable will be set.
#>
Export function Add-EnvPath {
	param(
			[ValidateScript({Test-Path -PathType Container $_})]
			[Parameter(Mandatory)]
			[string]
		$Directory,
			[switch]
		$Prepend,
			[switch]
		$Systemwide
	)
	
	$AbsPath = Resolve-Path $Directory
	Add-EnvVar "Path" $AbsPath -Prepend:$Prepend -Systemwide:$Systemwide
}

<#
.SYNOPSIS
	Adds directory to PSModulePath env variable.
.PARAMETER Directory
	Directory path to add.
.PARAMETER Systemwide
	If set, system-wide environment variable will be set (requires elevation).
	Otherwise, user environment variable will be set.
#>
Export function Add-EnvPSModulePath {
	param(
			[ValidateScript({Test-Path -PathType Container $_})]
			[Parameter(Mandatory)]
			[string]
		$Directory,
			[switch]
		$Prepend,
			[switch]
		$Systemwide
	)
	
	$AbsPath = Resolve-Path $Directory
	Add-EnvVar "PSModulePath" $AbsPath -Prepend:$Prepend -Systemwide:$Systemwide
}