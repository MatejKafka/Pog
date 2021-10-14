# Requires -Version 7
. $PSScriptRoot\..\header.ps1


$script:Confirms = @{}


Export function Confirm-Action {
	param(
			[Parameter(Mandatory)]
			[string]
		$Title,
			[Parameter(Mandatory)]
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
		switch ($Host.UI.PromptForChoice($Title, $Message, $Options, 0)) {
			0 {return $true} # Yes
			1 {return $false} # No
		}

	} else {
		$Options = @("&Yes", "Yes to &All", "&No", "No to A&ll")
		switch ($Host.UI.PromptForChoice($Title, $Message, $Options, 0)) {
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

Export function ConfirmOverwrite {
	param(
			[Parameter(Mandatory)]
			[string]
		$Title,
			[Parameter(Mandatory)]
			[string]
		$Message
	)

	# user passed -AllowOverwrite
	if ($global:_InternalArgs.AllowOverwrite) {
		return $true
	}
	return Confirm-Action $Title $Message "_AllowOverwrite"
}