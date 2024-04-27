using module .\..\lib\Utils.psm1
. $PSScriptRoot\..\lib\header.ps1

# always use basic parsing inside the generators, to ease compatibility with PowerShell 5
$PSDefaultParameterValues = @{
	"Invoke-WebRequest:UseBasicParsing" = $true
	"Invoke-RestMethod:UseBasicParsing" = $true
}

<# Retrieve all existing versions of a package by calling the package version generator script. #>
function RetrievePackageVersions([Pog.PackageGenerator]$Generator, $ExistingVersionSet) {
	foreach ($Obj in & $Generator.Manifest.ListVersionsSb $ExistingVersionSet) {
		# the returned object should either be the version string directly, or a map object
		#  (hashtable/pscustomobject/psobject/ordered) that has the Version property
		#  why not use -is? that's why: https://github.com/PowerShell/PowerShell/issues/16361
		$IsMap = $Obj.PSTypeNames[0] -in @("System.Collections.Hashtable", "System.Management.Automation.PSCustomObject", "System.Collections.Specialized.OrderedDictionary")
		$VersionStr = if (-not $IsMap) {$Obj} else {
			try {$Obj.Version}
			catch {
				throw "Version generator for package '$($Generator.PackageName)' returned a custom object without a Version property: $Obj" +`
					"  (Version generators must return either a version string, or a" +`
					" map container (hashtable, psobject, pscustomobject) with a Version property.)"
			}
		}

		if ([string]::IsNullOrEmpty($VersionStr)) {
			throw "Empty package version generated by the version generator for package '$($Generator.PackageName)' (either `$null or empty string)."
		}

		[pscustomobject]@{
			Version = [Pog.PackageVersion]$VersionStr
			# store the original value, so that we can pass it unchanged to the manifest generator
			OriginalValue = $Obj
		}
	}
}

# TODO: think what utility cmdlets can Pog reasonably provide and which is best left to existing tools like iwr/import
# FIXME: if -Force is passed, track if there are any leftover manifests (for removed versions) and delete them
Export function __main {
	param(
		[Pog.PackageGenerator]$Generator,
		[Pog.LocalRepositoryVersionedPackage]$Package,
		[string[]]$Version,
		[bool]$Force,
		[bool]$ListOnly
	)

	# list available versions without existing manifest (unless -Force is set, then all versions are listed)
	# only generate manifests for versions that don't already exist, unless -Force is passed
	$ExistingVersions = [System.Collections.Generic.HashSet[string]]::new($Package.EnumerateVersionStrings())
	$GeneratedVersions = RetrievePackageVersions $Generator $ExistingVersions `
		<# if -Force was not passed, filter out versions with already existing manifest #> `
		| ? {$Force -or -not $ExistingVersions.Contains($_.Version)} `
		<# if $Version was passed, filter out the versions; as the versions generated by the script
		   may have other metadata, we cannot use the versions passed in $Version directly #> `
		| ? {-not $Version -or $_.Version -in $Version}

	if ($Version -and @($Version).Count -ne @($GeneratedVersions).Count) {
		$FoundVersions = $GeneratedVersions | % {$_.Version}
		$MissingVersionsStr = ($Version | ? {$_ -notin $FoundVersions}) -join ", "
		throw "Some of the package versions passed in -Version were not found for package '$($Package.PackageName)': $MissingVersionsStr " +`
			"(Are you sure these versions exist?)"
		return
	}

	if ($ListOnly) {
		# useful for testing if all expected versions are retrieved
		return $GeneratedVersions | % {$Package.GetVersionPackage($_.Version, $false)}
	}

	# generate manifest for each version
	foreach ($v in $GeneratedVersions) {
		$p = $Package.GetVersionPackage($v.Version, $false)

		$TemplateData = if ($Generator.Manifest.GenerateSb) {
			# pass the value both as $_ and as a parameter, the scriptblock can accept whichever one is more convenient
			Invoke-DollarUnder $Generator.Manifest.GenerateSb $v.OriginalValue $v.OriginalValue
		} else {
			$v.OriginalValue # if no Generate block exists, forward the value emitted by ListVersions
		}

		$Count = @($TemplateData).Count
		if ($Count -ne 1) {
			throw "Manifest generator for package '$($p.PackageName)' generated " +`
				"$(if ($Count -eq 0) {"no output"} else {"multiple values"}) for version '$($p.Version)', expected a single [Hashtable]."
		}

		# unwrap the collection
		$TemplateData = @($TemplateData)[0]

		if ($TemplateData -isnot [Hashtable] -and $TemplateData -isnot [System.Collections.Specialized.OrderedDictionary]) {
			$Type = if ($TemplateData) {$TemplateData.GetType().ToString()} else {"null"}
			throw "Manifest generator for package '$($p.PackageName)' did not generate a [Hashtable] for version '$($p.Version)', got '$Type'."
		}

		# write out the manifest
		[Pog.ManifestTemplateFile]::SerializeSubstitutionFile($p.ManifestPath, $TemplateData)

		# manifest is not loaded yet, no need to reload
		echo $p
	}
}


Export function Get-GitHubRelease {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory, ValueFromPipeline)]
			[ValidatePattern("^[^/\s]+/[^/\s]+$")]
			[string[]]
		$Repository,
			### Retrieves tags instead of releases.
			[switch]
		$Tags
	)

	process {
		foreach ($r in $Repository) {
			$Endpoint = if ($Tags) {"tags"} else {"releases"}
			$Url = "https://api.github.com/repos/$r/$Endpoint"
			Write-Verbose "Listing GitHub releases for '$r'... (URL: $Url)"

			try {
				# piping through Write-Output enumerates the array returned by irm into individual values
				#  (see https://github.com/PowerShell/PowerShell/issues/15280)
				# -FollowRelLink automatically goes through all pages to get older releases
				Invoke-RestMethod -FollowRelLink $Url | Write-Output
			} catch [Microsoft.PowerShell.Commands.HttpResponseException] {
				$e = $_.Exception
				if ($e.StatusCode -eq 404) {
					throw "Cannot list $Endpoint for '$r', GitHub repository does not exist."
				} elseif ($e.StatusCode -eq 403 -and $e.Response.ReasonPhrase -eq "rate limit exceeded") {
					$Limit = try {$e.Response.Headers.GetValues("X-RateLimit-Limit")} catch {}
					$LimitMsg = if ($Limit) {" (at most $Limit requests/hour are allowed)"}
					throw "Cannot list $Endpoint for '$r', GitHub API rate limit exceeded$LimitMsg."
				} else {
					throw
				}
			}
		}
	}
}

Export function Get-HashFromChecksumFile {
	[CmdletBinding(DefaultParameterSetName="FileName", PositionalBinding=$false)]
	param(
			[Parameter(Mandatory, Position=0)]
			[uri]
		$Uri,
			[Parameter(Mandatory, Position=1, ParameterSetName="FileName")]
			[string]
		$FileName,
			[Parameter(Mandatory, ParameterSetName="Pattern")]
			[string]
		$Pattern
	)

	begin {
		if ($FileName) {
			$Pattern = [regex]::Escape($FileName)
		}

		# regex matches the almost-standard format generated by md5sum and sha*sum
		if ((Invoke-WebRequest $Uri) -notmatch ("(?:^|\n)([a-z0-9]+) +$Pattern(?:$|\n)")) {
			throw "Could not find the hash for file '$FileName' in the checksum file at '$Uri'."
		} else {
			return $Matches[1].ToUpperInvariant()
		}
	}
}