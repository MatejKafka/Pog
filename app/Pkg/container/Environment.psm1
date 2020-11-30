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
	
	$OldValue = [Environment]::GetEnvironmentVariable($VarName, $Target)
	if ($OldValue -eq $Value) {
		Write-Verbose "env:$VarName already set to '$Value'."
		return
	}
	
	if ($Systemwide) {
		Assert-Admin "Cannot write system environment variable $VarName without administrator privileges."
	}
	
	Write-Warning "Setting $Target environment variable env:$VarName to '$Value'..."
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

	$OldValue = [Environment]::GetEnvironmentVariable($VarName, $Target)
	
	if ($null -eq $OldValue) {
		# variable not set yet
		Set-EnvVar $VarName $Value -Systemwide:$Systemwide
		return
	}
	
	if ($OldValue.Split([IO.Path]::PathSeparator).Contains($Value)) {
		Write-Verbose "Value '$Value' already inserted in env:$VarName."
		return
	}
	
	if ($Systemwide) {
		Assert-Admin "Cannot update system environment variable $VarName without administrator privileges."
	}	
	Write-Warning "Adding '$Value' to env:$VarName..."
	
	$NewValue = if ($Prepend) {
		$Value + [IO.Path]::PathSeparator + $OldValue
	} else {
		$OldValue + [IO.Path]::PathSeparator + $Value
	}

	[Environment]::SetEnvironmentVariable($VarName, $NewValue, $Target)
	Update-EnvVar $VarName	
	Write-Information "Inserted '$Value' into env:$VarName."
}

<#
.SYNOPSIS
	Adds folder to system PATH env variable.
.PARAMETER Folder
	Folder to add.
.PARAMETER Systemwide
	If set, system environemnt variable will be set. Otherwise, user environment variable will be set.
#>
Export function Add-EnvPath {
	param(
			[ValidateScript({Test-Path -PathType Container $_})]
			[Parameter(Mandatory)]
			[string]
		$Folder,
			[switch]
		$Prepend,
			[switch]
		$Systemwide
	)

	$AbsPath = Resolve-Path $Folder
	Add-EnvVar "Path" $AbsPath -Prepend:$Prepend -Systemwide:$Systemwide
}