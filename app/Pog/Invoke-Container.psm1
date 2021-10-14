# Requires -Version 7
. $PSScriptRoot\header.ps1

Import-Module $PSScriptRoot"\Paths"
Import-Module $PSScriptRoot"\Utils"


enum ContainerType {
	Install
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


function Get-SetPreferenceVariables {
	# create copy, otherwise the values would dynamically change,
	#  as Get-Variable returns live reference
	$Out = @{}
	Get-Variable | ? {$_.Name -in $PreferenceVariableNames} `
		| % {$Out[$_.Name] = $_.Value}
	return $Out
}

function New-ScriptPosition {
	param($SrcFile, $LineNum, $ColumnNum, $Line)
	return [System.Management.Automation.Language.ScriptPosition]::new(
			$SrcFile, $LineNum, $ColumnNum, $Line, $null)
}

<# 
 Adds src script info to reconstructed error record.
 #>
function Set-ErrorSrcFile {
	param($Err, $SrcFile)

	$Src = $SrcFile + ":?"
	$Line = $Err.InvocationInfo.Line
	$Field = [System.Management.Automation.InvocationInfo].GetField("_scriptPosition", "static,nonpublic,instance")
	$Extent = $Field.GetValue($Err.InvocationInfo)

	$Err.InvocationInfo.DisplayScriptPosition = [System.Management.Automation.Language.ScriptExtent]::new(
		(New-ScriptPosition $Src $Extent.StartLineNumber $Extent.StartColumnNumber $Line),
		(New-ScriptPosition $Src $Extent.EndLineNumber $Extent.EndColumnNumber $Line)
	)
}

Export function Invoke-Container {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ContainerType]
		$ContainerType,
			# FIXME: TOCTOU (Import-PowerShellDataFile vs Invoke-Expression),
			#  but it's not that serious, as we are executing the content anyway,
			#  so it doesn't really open up a security vulnerability
			[Parameter(Mandatory)]
			[ValidateScript({Test-Path $_})]
			[string]
		$ManifestPath,
			[Parameter(Mandatory)]
			[ValidateScript({Test-Path $_})]
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
	$PrefVars = Get-SetPreferenceVariables

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

	$ContainerJob = Start-Job -WorkingDirectory $WorkingDirectory -FilePath $CONTAINER_SCRIPT `
			-InitializationScript ([ScriptBlock]::Create(". $CONTAINER_SETUP_SCRIPT $ContainerType")) `
			-ArgumentList @($ManifestPath, $ContainerType, $InternalArguments, $ScriptArguments, $PrefVars)

	try {
		# FIXME: this breaks error source
		# FIXME: Original error type is lost (changed to generic "Exception")

		# hackaround for https://github.com/PowerShell/PowerShell/issues/7814
		# it seems that the duplication is caused by Receive-Job sending
		#  original information messages through, and also emitting them again as new messages
		Receive-Job -Wait $ContainerJob -InformationAction "SilentlyContinue"
	} finally {
		Stop-Job $ContainerJob
		Receive-Job -Wait $ContainerJob -InformationAction "SilentlyContinue"
		Remove-Job $ContainerJob
	}
}