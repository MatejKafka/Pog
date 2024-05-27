using module .\Utils.psm1
. $PSScriptRoot\header.ps1

Export-ModuleMember -Cmdlet Remove-EnvVarEntry

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


Export function Set-EnvVar {
	### .SYNOPSIS
	### 	Sets an environment variable.
	[CmdletBinding()]
	param(
			### Name of the modified environment variable.
			[Parameter(Mandatory)]
			[string]
		$VarName,
			### The value to set.
			[Parameter(Mandatory)]
			[string]
		$Value
	)

	$OldValue = [Environment]::GetEnvironmentVariable($VarName, [System.EnvironmentVariableTarget]::User)
	if ($OldValue -eq $Value) {
		Write-Verbose "'env:$VarName' already set to '$Value' for the current user."
		# the var might be set in system, but our process might have the old/no value
		# this ensures that after this call, value of $env:VarName is up to date
		[Environment]::SetEnvironmentVariable($VarName, $Value, [System.EnvironmentVariableTarget]::Process)
		return
	}

	Write-Warning "Setting environment variable 'env:$VarName' to '$Value' for the current user..."

	[Environment]::SetEnvironmentVariable($VarName, $Value, [System.EnvironmentVariableTarget]::User)
	# also set the variable for the current process
	[Environment]::SetEnvironmentVariable($VarName, $Value, [System.EnvironmentVariableTarget]::Process)
}

function CombineEnvValues($OldValue, $AddedValue, [switch]$Prepend) {
	if ($Prepend) {
		return $AddedValue + [IO.Path]::PathSeparator + $OldValue
	} else {
		return $OldValue + [IO.Path]::PathSeparator + $AddedValue
	}
}

function AddToProcessEnv($VarName, $Value, [switch]$Prepend) {
	# this may not match the position of the added global env var exactly, but better than nothing
	$ProcessEnv = [Environment]::GetEnvironmentVariable($VarName, [System.EnvironmentVariableTarget]::Process)
	if (-not $ProcessEnv.Split([IO.Path]::PathSeparator).Contains($Value)) {
		$ProcessEnv = CombineEnvValues $ProcessEnv $Value -Prepend:$Prepend
		[Environment]::SetEnvironmentVariable($VarName, $ProcessEnv, [System.EnvironmentVariableTarget]::Process)
	}
}

Export function Add-EnvVar {
	### .SYNOPSIS
	### 	Appends an item to a given user-level environment variable.
	[CmdletBinding()]
	param(
			### Name of the modified environment variable.
			[Parameter(Mandatory)]
			[string]
		$VarName,
			### The value to add.
			[Parameter(Mandatory)]
			[string]
		$Value,
			[switch]
		$Prepend
	)

	$OldValue = [Environment]::GetEnvironmentVariable($VarName, [System.EnvironmentVariableTarget]::User)

	if ($null -eq $OldValue) {
		# variable not set yet
		Set-EnvVar $VarName $Value
		return
	}

	# FIXME: check for trailing / or \
	if ($OldValue.Split([IO.Path]::PathSeparator).Contains($Value)) {
		Write-Verbose "Value '$Value' already present in 'env:$VarName' for the current user."
		# the var might be set in system, but our process might have the old/no value
		# this ensures that after this call, value of $env:VarName is up to date
		AddToProcessEnv $VarName $Value -Prepend:$Prepend
		return
	}

	Write-Warning "Adding '$Value' to 'env:$VarName' for the current user..."

	$NewValue = CombineEnvValues $OldValue $Value -Prepend:$Prepend

	[Environment]::SetEnvironmentVariable($VarName, $NewValue, [System.EnvironmentVariableTarget]::User)

	# also set the variable for the current process
	# do not reload the variable, since that could remove process-scope modifications
	AddToProcessEnv $VarName $Value -Prepend:$Prepend

	$Verb = if ($Prepend) {"Prepended"} else {"Appended"}
	Write-Information "$Verb '$Value' to 'env:$VarName'."
}
