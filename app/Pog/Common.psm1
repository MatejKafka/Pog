# Requires -Version 7
<# Module with opinionated, package-related support functions. #>
using module ./Paths.psm1
using module ./lib_compiled/Pog.dll
using module ./lib/Utils.psm1
using module ./lib/Convert-CommandParametersToDynamic.psm1
. $PSScriptRoot\lib\header.ps1


Export function Get-PackageDirectory {
	param(
		[Parameter(Mandatory)]$PackageName,
		[switch]$NoError
	)

	$SearchedPaths = @()
	foreach ($Root in $PATH_CONFIG.PackageRoots.ValidPackageRoots) {
		$PackagePath = Resolve-VirtualPath (Join-Path $Root $PackageName)
		$SearchedPaths += $PackagePath
		if (Test-Path -Type Container $PackagePath) {
			return Get-Item $PackagePath
		}
	}

	if ($NoError) {
		return $null
	}
	throw ("Could not find package '$PackageName' in known package directories. " `
			+ "Searched paths:`n" + [string]::Join("`n", $SearchedPaths))
}

Export function Import-PackageManifestFile {
	param(
			[Parameter(Mandatory)]
			[Pog.Package]
		$Package,
			[switch]
		$NoUnwrap
	)

	if (-not $Package.ManifestExists) {
		throw "Manifest file is missing for package '$($Package.PackageName)'. Searched path: $($Package.ManifestPath)"
	}

	try {
		# PowerShell has a bug where it double wraps script blocks in .psd1 files: https://github.com/PowerShell/PowerShell/issues/12789
		# to avoid this issue, we first import the manifest using Import-PowerShellDataFile to check that it's valid,
		# and then evaluate if using Invoke-Expression to get the actual manifest
		$Manifest = Import-PowerShellDataFile $Package.ManifestPath
		if ($NoUnwrap) {
			return $Manifest
		} else {
			return Invoke-Expression (Get-Content -Raw $Package.ManifestPath)
		}
	} catch {
		# TODO: better error messages (there's an open issue for that)
		throw [Exception]::new("Could not load package manifest from '$($p.ManifestPath)', " +`
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
			[string]
		$NamePrefix = ""
	)

	if ($PSCmdlet.ParameterSetName -eq "PackageName") {
		try {
			$p = [Pog.ImportedPackageRaw]::CreateResolved((Get-PackageDirectory $PackageName))
			$Manifest = Import-PackageManifestFile $p
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

		$Sb = $Manifest[$PropertyName]
	} else {
		$Sb = $ScriptBlock
	}

	# we cannot extract parameters directly from a scriptblock
	# instead, we'll turn it into a temporary function and use Get-Command to read the parameters
	# see https://github.com/PowerShell/PowerShell/issues/13774

	# this is only set in local scope, no need to clean up
	$function:TmpFn = $Sb
	$FnInfo = Get-Command -Type Function TmpFn

	$RuntimeDict = Convert-CommandParametersToDynamic $FnInfo -NoPositionAttribute -NamePrefix $NamePrefix

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


function Confirm-ManifestInstallHashtable($InstallBlock) {
	$I = $InstallBlock
	$Issues = @()
	# TODO: better checking (currently, only SourceUrl/Url and ExpectedHash/Hash are checked, and other keys are ignored)

	# SOURCE URL
	if (-not $I.ContainsKey("SourceUrl") -and -not $I.ContainsKey("Url")) {
		$Issues += "Missing 'Url'/'SourceUrl' key in 'Install' hashtable - it should contain URL of the archive which is downloaded during installation."
	} elseif ($I.ContainsKey("SourceUrl") -and $I.ContainsKey("Url")) {
		$Issues += "'Install.SourceUrl' and 'Install.Url' are aliases for the same parameter, only one must be defined."
	}
	foreach ($Prop in @("SourceUrl", "Url")) {
		if ($I.ContainsKey($Prop) -and $I[$Prop].GetType() -notin @([string], [ScriptBlock])) {
			$Issues += "'Install.$Prop' must be either string URL, or a ScriptBlock that returns the URL string, got '$($I[$Prop].GetType())'."
		}
	}

	# EXPECTED HASH
	if ($I.ContainsKey("Hash") -and $I.ContainsKey("ExpectedHash")) {
		$Issues += "'Install.Hash' and 'Install.ExpectedHash' are aliases for the same parameter, at most one may be defined."
	}
	foreach ($Prop in @("Hash", "ExpectedHash")) {
		if ($I.ContainsKey($Prop)) {
			if ($I[$Prop] -isnot [string]) {
				$Issues += "'Install.$Prop' must be a string (SHA-256 hash), if present - got '$($I[$Prop].GetType())'."
			} elseif ($I[$Prop] -ne "" -and $I[$Prop] -notmatch '^[a-fA-F0-9]{64}$') {
				$Issues += "'Install.$Prop' must be a SHA-256 hash (64 character hex string), got '$($I[$Prop])'."
			}
		}
	}

	return $Issues
}


Export function Confirm-Manifest {
	param(
			[Parameter(Mandatory)]
			[Hashtable]
		$Manifest,
			[string]
		$ExpectedName,
			[string]
		$ExpectedVersion,
			[switch]
		$IsRepositoryManifest
	)

	$RequiredKeys = @{
		Name = [string]; Version = [string]; Architecture = @([string], [Object[]]);
		Enable = [scriptblock]; Install = @([scriptblock], [Hashtable], [string])
	}

	$OptionalKeys = @{
		Private = [bool]; Description = [string]; Website = [string]; Channel = [string]
	}

	$Issues = @()


	if ("Private" -in $Manifest.Keys -and $Manifest.Private) {
		if ($IsRepositoryManifest) {
			$Issues += "Property 'Private' is not allowed in manifests in a package repository."
		} else {
			Write-Verbose "Skipped validation of private package manifest '$ExpectedName'."
			return
		}
	}

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

	if ($Manifest.ContainsKey("Install") -and $Manifest.Install -is [hashtable]) {
		# check Install key structure
		$Issues += Confirm-ManifestInstallHashtable $Manifest.Install
	}

	if ($Issues.Count -gt 1) {
		throw ("Multiple issues encountered when validating manifest:`n`t" + ($Issues -join "`n`t"))
	} elseif ($Issues.Count -eq 1) {
		throw $Issues
	}

	Write-Verbose "Manifest is valid."
}
