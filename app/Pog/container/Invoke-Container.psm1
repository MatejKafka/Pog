# Requires -Version 7
using module ..\Paths.psm1
using module ..\lib\Utils.psm1
. $PSScriptRoot\..\lib\header.ps1


enum ContainerType {
	Install
	GetInstallHash
	Enable
}

$PreferenceVariableNames = @(
	"ConfirmPreference"
	"DebugPreference"
	"ErrorView"
	"FormatEnumerationLimit"

	"LogCommandHealthEvent"
	"LogCommandLifecycleEvent"
	"LogEngineHealthEvent"
	"LogEngineLifecycleEvent"
	"LogProviderHealthEvent"
	"LogProviderLifecycleEvent"

<# these don't seem like something an install script should know about
	"PSEmailServer"
	"PSSessionApplicationName"
	"PSSessionConfigurationName"
	"PSSessionOption"
#>
	"ProgressPreference"
	"VerbosePreference"
	"InformationPreference"
	"WarningPreference"
)


function Get-SetPreferenceVariable {
	# create copy, otherwise the values would dynamically change,
	#  as Get-Variable returns live reference
	$Out = @{}
	Get-Variable | ? {$_.Name -in $PreferenceVariableNames} `
		| % {$Out[$_.Name] = $_.Value}
	return $Out
}


Export function Invoke-Container {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ContainerType]
		$ContainerType,
			[Parameter(Mandatory)]
			[string]
		$PackageName,
			# FIXME: TOCTOU (Import-PowerShellDataFile vs Invoke-Expression),
			#  but it's not that serious, as we are executing the content anyway,
			#  so it doesn't really open up a security vulnerability
			[Parameter(Mandatory)]
			[ValidateScript({Test-Path -Type Leaf $_})]
			[string]
		$ManifestPath,
			[Parameter(Mandatory)]
			[ValidateScript({Test-Path -Type Container $_})]
			[string]
		$WorkingDirectory,
			[Parameter(Mandatory)]
			[Hashtable]
		$InternalArguments,
			[Parameter(Mandatory)]
			[Hashtable]
		$ScriptArguments
	)

	Get-CallerPreference -Cmdlet $PSCmdlet -SessionState $ExecutionContext.SessionState
	$PrefVars = Get-SetPreferenceVariable

	if ($PrefVars.DebugPreference -eq "Continue") {
		# see below
		$PrefVars.VerbosePreference = "Continue"
	}

	if ($PrefVars.VerbosePreference -eq "Continue") {
		# if verbose prints are active, also activate information streams
		# TODO: this is quite convenient, but it kinda goes against
		#  the original intended use of these variables, is it really good idea?
		$PrefVars.InformationPreference = "Continue"
	}

	# Start-ThreadJob is only supported in PowerShell Core (pwsh)
	$UseThreadJob = [bool](Get-Command Start-ThreadJob -ErrorAction Ignore)
	$ContainerArgs = @($ContainerType, $PackageName, $ManifestPath, $InternalArguments, $ScriptArguments, $PrefVars)

	$ContainerJob = if ($UseThreadJob) {
		# the indirect scriptblock creation is used to avoid https://github.com/PowerShell/PowerShell/issues/15096
		#  and have a correct $PSScriptRoot inside the container
		Start-ThreadJob {
			Set-Location $using:WorkingDirectory
			$global:InteractiveHost = $using:Host
			. "$using:PSScriptRoot\container.ps1" @using:ContainerArgs
		}
	} else {
		Write-Debug "Using 'Start-Job' to run the Pog container, 'Start-ThreadJob' is not supported."
		Start-Job -WorkingDirectory $WorkingDirectory {
			. "$using:PSScriptRoot\container.ps1" @using:ContainerArgs
		}
	}

	try {
		# hackaround for https://github.com/PowerShell/PowerShell/issues/7814
		# it seems that the duplication is caused by Receive-Job sending
		#  original information messages through, and also emitting them again as new messages
		Receive-Job -Wait $ContainerJob
	} finally {
		Stop-Job $ContainerJob
		Receive-Job -Wait $ContainerJob
		Remove-Job $ContainerJob
	}
}
