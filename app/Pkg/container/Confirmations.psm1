. $PSScriptRoot\..\header.ps1


$script:AllowOverwriteOverride = $null

Export function ConfirmOverwrite {
	param(
			[Parameter(Mandatory)]
			[string]
		$Title,
			[Parameter(Mandatory)]
			[string]
		$Message,
			[Parameter(Mandatory)]
			[string]
		$ErrorMessage
	)

	# user selected Yes/No to All in previous confirmation
	if ($null -ne $script:AllowOverwriteOverride) {
		return $script:AllowOverwriteOverride
	}
	# user passed -AllowOverwrite
	if ($global:Pkg_AllowOverwrite) {
		return $true
	}
	
	$Options = @("&Yes", "Yes to &All", "&No", "No to A&ll", "&Exit")
	switch ($Host.UI.PromptForChoice($Title, $Message, $Options, 0)) {
		4 {throw $ErrorMessage} # Exit
		0 {return $true} # Yes
		2 {return $false} # No
		1 { # Yes to All
			$script:AllowOverwriteOverride = $true
			return $true
		}
		3 { # No to All
			$script:AllowOverwriteOverride = $false
			return $false
		}
	}
}