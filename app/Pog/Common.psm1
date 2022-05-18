# Requires -Version 7
<#
Module with opinionated, package-related support functions.
#>

. $PSScriptRoot\lib\header.ps1

Import-Module $PSScriptRoot"\Paths"
Import-Module $PSScriptRoot"\lib\Utils"
Import-Module $PSScriptRoot"\lib\VersionParser"
Import-Module $PSScriptRoot"\lib\Convert-CommandParametersToDynamic"

Export-ModuleMember -Function New-PackageVersion


Export function Get-LatestPackageVersion {
	param (
			[Parameter(Mandatory)]
			[ValidateScript({Test-Path $_})]
		$PackagePath
	)
	return Get-LatestVersion (ls $PackagePath -Directory).Name
}

Export function Get-PackagePath {
	param(
		[Parameter(Mandatory)]$PackageName,
		[switch]$NoError
	)

	$SearchedPaths = @()
	foreach ($Root in $PACKAGE_ROOTS) {
		$PackagePath = Resolve-VirtualPath (Join-Path $Root $PackageName)
		$SearchedPaths += $PackagePath
		if (Test-Path -Type Container $PackagePath) {
			return $PackagePath
		}
	}

	if ($NoError) {
		return $null
	}
	throw ("Could not find package '$PackageName' in known package directories. " `
			+ "Searched paths:`n" + [String]::Join("`n", $SearchedPaths))
}

Export function Get-ManifestPath {
	param(
		[Parameter(Mandatory)]$PackagePath,
		[switch]$NoError
	)

	$SearchedPaths = @()
	foreach ($ManifestRelPath in $MANIFEST_PATHS) {
		$ManifestPath = Resolve-VirtualPath (Join-Path $PackagePath $ManifestRelPath)
		if (Test-Path -Type Leaf $ManifestPath) {
			return Get-Item $ManifestPath
		}
		$SearchedPaths += $ManifestPath
	}

	if ($NoError) {
		return $null
	}
	$PackageName = Split-Path $PackagePath -Leaf
	throw ("Could not find manifest file for package '$PackageName'. " `
			+ "Searched paths:`n" + [String]::Join("`n", $SearchedPaths))
}

Export function Import-PackageManifestFile {
	param(
		[Parameter(Mandatory)]$ManifestPath
	)

	if (-not (Test-Path -Type Leaf $ManifestPath)) {
		throw "Requested manifest file does not exist: '$ManifestPath'."
	}

	try {
		return Import-PowerShellDataFile $ManifestPath
	} catch {
		# TODO: better error messages (there's an open issue for that)
		throw [Exception]::new("Could not load package manifest from '$ManifestPath', " +`
				"it is not a valid PowerShell data file.", $_.Exception)
	}
}

# takes a package name, and returns all static parameters of a selected setup script as a runtime parameter dictionary
# keyword-only, no positional arguments; aliases are supported
Export function Copy-ManifestParameters {
	[CmdletBinding(DefaultParameterSetName = "PackageName")]
	param(
			[Parameter(Mandatory, ParameterSetName = "PackageName", Position = 0)]
			[string]
		$PackageName,
			# either Install or Enable
			[Parameter(Mandatory, ParameterSetName = "PackageName", Position = 1)]
			[string]
		$PropertyName,
			[Parameter(Mandatory, ParameterSetName = "ScriptBlock")]
			[ScriptBlock]
		$ScriptBlock,
			[Parameter(Mandatory)]
			[string]
		$NamePrefix
	)

	if ($PSCmdlet.ParameterSetName -eq "PackageName") {
		try {
			$PackagePath = Get-PackagePath $PackageName
			$ManifestPath = Get-ManifestPath $PackagePath
			$Manifest = Import-PackageManifestFile $ManifestPath
		} catch {
			# probably incorrect package name, ignore it here
			return $null
		}

		if ($Manifest[$PropertyName] -isnot [scriptblock]) {
			# not a script block, doesn't have parameters
			return @{
				Parameters = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()
				ExtractFn = {return @{}}
			}
		}

		# we execute the manifest script block, as powershell has a bug where it double wraps it for some reason
		#  see https://github.com/PowerShell/PowerShell/issues/12789
		# when this bug is fixed, this will break; it also needs to be fixed in container/container.ps1
		$Sb = & $Manifest[$PropertyName]
	} else {
		# see above
		$Sb = & $ScriptBlock
	}

	# we cannot extract parameters directly from scriptblock
	# instead, we'll turn it into a temporary function and use Get-Command to read the parameters
	# see https://github.com/PowerShell/PowerShell/issues/13774

	# this is only set in local scope, no need to clean up
	$function:TmpFn = $Sb
	$Params = (Get-Command -Type Function TmpFn).Parameters

	$RuntimeDict = Convert-CommandParametersToDynamic $Params -AllowAliases -NamePrefix $NamePrefix

	$ExtractAddedParameters = {
		param([Parameter(Mandatory)]$_PSBoundParameters)

		$Extracted = @{}
		$RuntimeDict.Keys `
			| ? {$_PSBoundParameters.ContainsKey($_)} `
			| % {$Extracted[$_.Substring($NamePrefix.Length)] = $_PSBoundParameters[$_]}

		return $Extracted
	}.GetNewClosure()

	return @{
		Parameters = $RuntimeDict
		ExtractFn = $ExtractAddedParameters
	}
}


Export function Confirm-Manifest {
	param(
			[Parameter(Mandatory)]
			[Hashtable]
		$Manifest,
			[string]
		$ExpectedName,
			[string]
		$ExpectedVersion
	)

	if ("Private" -in $Manifest.Keys -and $Manifest.Private) {
		Write-Verbose "Skipped validation of private package manifest '$Manifest.Name'."
		return
	}

	$RequiredKeys = @{
		Name = [string]; Version = [string]; Architecture = @([string], [Object[]]);
		Enable = [scriptblock]; Install = @([scriptblock], [Hashtable], [string])
	}

	$OptionalKeys = @{
		Description = [string]; Website = [string]; Channel = [string]
	}


	$Issues = @()

	$RequiredKeys.GetEnumerator() | % {
		$StrTypes = $_.Value -join " | "
		if (!$Manifest.ContainsKey($_.Key)) {
			$Issues += "Missing manifest property '$($_.Key)' of type '$StrTypes'."
			return
		}
		$RealType = $Manifest[$_.Key].GetType()
		if ($RealType -notin $_.Value) {
			$Issues += "Property '$($_.Key)' is present, but has incorrect type '$RealType', expected '$StrTypes'."
		}
	}

	$OptionalKeys.GetEnumerator() | ? {$Manifest.ContainsKey($_.Key)} | % {
		$RealType = $Manifest[$_.Key].GetType()
		if ($RealType -notin $_.Value) {
			$StrTypes = $_.Value -join " | "
			$Issues += "Optional property '$($_.Key)' is present, but has incorrect type '$RealType', expected '$StrTypes'."
		}
	}

	$AllowedKeys = $RequiredKeys.Keys + $OptionalKeys.Keys
	$Manifest.Keys | ? {-not $_.StartsWith("_")} | ? {$_ -notin $AllowedKeys} | % {
		$Issues += "Found unknown property '$_' - private properties must be prefixed with underscore ('_PrivateProperty')."
	}


	if ($Manifest.ContainsKey("Name")) {
		if (-not [string]::IsNullOrEmpty($ExpectedName) -and $Manifest.Name -ne $ExpectedName) {
			$Issues += "Incorrect 'Name' property value - got '$($Manifest.Name)', expected '$ExpectedName'."
		}
	}

	if ($Manifest.ContainsKey("Version")) {
		if (-not [string]::IsNullOrEmpty($ExpectedVersion) -and $Manifest.Version -ne $ExpectedVersion) {
			$Issues += "Incorrect 'Version' property value - got '$($Manifest.Version)', expected '$ExpectedVersion'."
		}
	}

	if ($Manifest.ContainsKey("Architecture")) {
		$ValidArch = @("x64", "x86", "*")
		if (@($Manifest.Architecture | ? {$_ -notin $ValidArch}).Count -gt 0) {
			$Issues += "Invalid 'Architecture' value - got '$($Manifest.Architecture)', expected one of $ValidArch, or an array."
		}
	}

	# TODO: better checking (currently, only Url and Hash are checked, and other keys are ignored)
	if ($Manifest.ContainsKey("Install") -and $Manifest.Install -is [hashtable]) {
		# check Install key structure
		if (-not $Manifest.Install.ContainsKey("Url")) {
			$Issues += "Missing 'Url' key in 'Install' hashtable - it should contain URL of the archive which is downloaded during installation."
		} elseif ($Manifest.Install.Url.GetType() -notin @([string], [ScriptBlock])) {
			$Issues += "'Install.Url' must be either string URL, or a ScriptBlock that returns the URL string, got '$($Manifest.Install.Url.GetType())'."
		}
		if ($Manifest.Install.ContainsKey("Hash")) {
			if ($Manifest.Install.Hash -isnot [string]) {
				$Issues += "'Install.Hash' must be a string (SHA256 hash), if present - got '$($Manifest.Install.Hash.GetType())'."
			} elseif ($Manifest.Install.Hash -ne "?" -and $Manifest.Install.Hash -notmatch '^(\-|[a-fA-F0-9]{64})$') {
				$Issues += "'Install.Hash' must be a SHA256 hash (64 character hex string), or '?' - got '$($Manifest.Install.Hash)'."
			}
		}
	}

	if ($Issues.Count -gt 1) {
		throw ("Multiple issues encountered when validating manifest:`n`t" + ($Issues -join "`n`t"))
	} elseif ($Issues.Count -eq 1) {
		throw $Issues
	}

	Write-Verbose "Manifest is valid."
}