. $PSScriptRoot\header.ps1

Import-Module $PSScriptRoot"\Paths"


enum ContainerEnvType {
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
	
	"PSEmailServer"
	
	"PSSessionApplicationName"
	"PSSessionConfigurationName"
	"PSSessionOption"
	
	"ProgressPreference"
	"VerbosePreference"
	"InformationPreference"
	"WarningPreference"
)


function Get-SetPreferenceVariables {
	return Get-Variable | ? {$_.Name -in $PreferenceVariableNames}
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
	param(
			[Parameter(Mandatory)]
			[string]
		$WorkingDirectory,
			[Parameter(Mandatory)]
			[string]
		$ScriptFile,
			[Parameter(Mandatory)]
			[ContainerEnvType]
		$EnvType,
			[Parameter(Mandatory)]
			[ScriptBlock]
		$ScriptBlock,
			[Parameter(Mandatory)]
			[Hashtable]
		$InternalArguments,
			[Parameter(Mandatory)]
			[Hashtable]
		$ScriptArguments
	)

	$PrefVars = Get-SetPreferenceVariables
	
	$ContainerJob = Start-Job -WorkingDirectory $WorkingDirectory -FilePath $CONTAINER_SCRIPT `
			-InitializationScript ([ScriptBlock]::Create(". $CONTAINER_SETUP_SCRIPT $EnvType")) `
			-ArgumentList @($ScriptBlock, $InternalArguments, $ScriptArguments, $PrefVars)
	
	try {
		# FIXME: this breaks error source
		# FIXME: Original error type is lost (changed to generic "Exception")
		Receive-Job -Wait $ContainerJob
	} finally {
		Stop-Job $ContainerJob
		Remove-Job $ContainerJob
	}
}