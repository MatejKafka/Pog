# Requires -Version 7
<#
	.SYNOPSIS
	A utility module for displaying confirmation dialogs to user and tracking
	"Yes/No to All" responses. Used both inside the manifest container and outside (in Pog.psm1).
#>
. $PSScriptRoot\lib\header.ps1

# if this module is used inside the container, it runs in a separate runspace with a possibly
#  non-interactive $Host; if that's the case, $global:InteractiveHost is set to the original,
#  interactive $Host instance in `container\Invoke-Container.psm1` during container setup,
#  and we use it instead of the non-interactive one for displaying prompts
if (-not (Test-Path Variable:global:InteractiveHost)) {
	$script:InteractiveHost = $Host
}

# outside container, this is session-scoped
# inside container, this is invocation-scoped
$script:Confirms = @{}


# TODO: should we use ShouldProcess/ShouldContinue instead of a custom prompt?
Export function Confirm-Action {
	param(
			[Parameter(Mandatory)]
			[string]
		$Title,
			[Parameter(Mandatory)]
			[AllowEmptyString()]
			[string]
		$Message,
			# if passed, options "Yes to All" and "No to All" are added and stored
			# all later confirmation prompts with the same ActionType are then automatically resolved
			[string]
		$ActionType
	)

	# user selected Yes/No to All in previous confirmation
	if ($script:Confirms.ContainsKey($ActionType) -and $null -ne $script:Confirms[$ActionType]) {
		$Value = $script:Confirms[$ActionType]
		Write-Debug "Skipping confirmation for action type '$ActionType', user previously selected '$Value'."
		return $Value
	}

	if ([string]::IsNullOrEmpty($ActionType)) {
		$Options = @("&Yes", "&No")
		switch ($InteractiveHost.UI.PromptForChoice($Title, $Message, $Options, 0)) {
			0 {return $true} # Yes
			1 {return $false} # No
		}

	} else {
		$Options = @("&Yes", "Yes to &All", "&No", "No to A&ll")
		switch ($InteractiveHost.UI.PromptForChoice($Title, $Message, $Options, 0)) {
			0 {return $true} # Yes
			2 {return $false} # No
			1 { # Yes to All
				$script:Confirms[$ActionType] = $true
				return $true
			}
			3 { # No to All
				$script:Confirms[$ActionType] = $false
				return $false
			}
		}
	}
	throw "Unexpected prompt option selected. Seems like Pog developers fucked something up, plz send bug report."
}
