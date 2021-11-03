# Requires -Version 7
. $PSScriptRoot\lib\header.ps1

Import-Module $PSScriptRoot"\Paths"
Import-Module $PSScriptRoot"\lib\Utils"
Import-Module $PSScriptRoot"\Common"
Import-Module $PSScriptRoot"\Confirmations"
Import-Module $PSScriptRoot"\Invoke-Container"


class ImportedPackageName : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return ls $script:PACKAGE_ROOTS -Directory | select -ExpandProperty Name
    }
}

class RepoPackageName : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return ls $script:MANIFEST_REPO -Directory | select -ExpandProperty Name
    }
}

class RepoManifestGeneratorName : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return ls $script:MANIFEST_GENERATOR_REPO -Directory | select -ExpandProperty Name
    }
}

class PackageRoot : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
		return $script:PACKAGE_ROOTS + $script:UNRESOLVED_PACKAGE_ROOTS
    }
}


function Export-AppShortcuts {
	param(
		[Parameter(Mandatory)]$AppPath,
		[Parameter(Mandatory)]$ExportPath
	)

	ls -File -Filter "*.lnk" $AppPath | % {
		Copy-Item $_ -Destination $ExportPath
		echo "Exported shortcut '$($_.Name)' from '$(Split-Path -Leaf $AppPath)'."
	}
}


Export function Export-ShortcutsToStartMenu {
	[CmdletBinding()]
	param(
			[switch]
		$UseSystemWideMenu,
			[switch]
		$ExcludeUnderscoredPackages
	)

	$TargetDir = if ($UseSystemWideMenu) {
		Join-Path $SYSTEM_START_MENU $APP_NAME
	} else {
		Join-Path $USER_START_MENU $APP_NAME
	}

	echo "Exporting shortcuts to '$TargetDir'."

	if (Test-Path $TargetDir) {
		echo "Clearing previous $APP_NAME start menu entries..."
		Remove-Item -Recurse $TargetDir
	}
	$null = New-Item -ItemType Directory $TargetDir

	ls $PACKAGE_ROOTS -Directory | ? {
		-not ($ExcludeUnderscoredPackages -and $_.Name.StartsWith("_"))
	} | % {
		Export-AppShortcuts $_.FullName $TargetDir
	}
}


Export function Get-RepositoryPackage {
	[CmdletBinding()]
	param()
	return [RepoPackageName]::new().GetValidValues()
}

Export function Get-Package {
	[CmdletBinding()]
	param()
	return [ImportedPackageName]::new().GetValidValues()
}


function FlushPackageRootList {
	($PACKAGE_ROOTS + $UNRESOLVED_PACKAGE_ROOTS) | Set-Content $PACKAGE_ROOT_FILE
}

Export function Get-Root {
	return $PACKAGE_ROOTS + $UNRESOLVED_PACKAGE_ROOTS
}

Export function New-Root {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateScript({Test-Path $_ -Type Container})]
			[string]
		$RootDir
	)

	$Resolved = Resolve-Path $RootDir
	if ($Resolved.Path -in $PACKAGE_ROOTS) {
		return "Passed path is already a package root: $Resolved."
	}

	[void]$PACKAGE_ROOTS.Add($Resolved.Path)
	FlushPackageRootList
	return "Added $Resolved as package root."
}

Export function Remove-Root {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([PackageRoot], IgnoreCase = $false)]
			[string]
		$RootDir
	)

	$Resolved = Resolve-VirtualPath $RootDir

	if ($UNRESOLVED_PACKAGE_ROOTS.Contains($Resolved)) {
		$UNRESOLVED_PACKAGE_ROOTS.Remove($Resolved)
		FlushPackageRootList
		return "Removed unresolved package root $Resolved."
	}

	$PACKAGE_ROOTS.Remove($Resolved)
	FlushPackageRootList
	return "Removed $Resolved from package root list."
}


<# Remove cached package archives older than the provided date.
   TODO: Show actual package name and version here, not the file name (which may be quite random).
    Probably write a metadata file into the directory when creating the cache entry.
	Issue: In the improbable case of multiple packages using the same cache entry,
	we must append to the cache entry metadata file, not overwrite
   #>
Export function Clear-DownloadCache {
	[CmdletBinding(DefaultParameterSetName = "Days")]
	param(
			[Parameter(Mandatory, ParameterSetName = "Date", Position = 0)]
			[DateTime]
		$DateBefore,
			[Parameter(Mandatory, ParameterSetName = "Days", Position = 0)]
			[int]
		$DaysBefore
	)

	if ($PSCmdlet.ParameterSetName -eq "Days") {
		$DateBefore = [DateTime]::Now.AddDays(-$DaysBefore)
	}

	$RemovedEntries = ls -Directory $DOWNLOAD_CACHE_DIR | ? {$_.LastWriteTime -le $DateBefore}

	if (@($RemovedEntries).Count -eq 0) {
		throw "No cached package archives downloaded before '$($DateBefore.ToString())' found."
	}

	$SizeSum = 0
	$RemovedEntries |
		% {ls -File $_} |
		sort Length -Descending |
		% {$SizeSum += $_.Length; echo $_} |
		% {"{0,10:F2} MB - {1}" -f @(($_.Length / 1MB), $_.Name)}

	$Title = "Remove the listed package archives, freeing ~{0:F} GB of space?" -f ($SizeSum / 1GB)
	$Message = "This will not affect installed applications. Reinstallation of an application may take longer," + `
		" as the package will have to be downloaded again."
	if (Confirm-Action $Title $Message) {
		$RemovedEntries | rm -Recurse
	}
}


Export function Enable- {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName,
			[Hashtable]
		$PackageParameters = @{},
			# allows overriding existing commands without confirmation
			[switch]
		$AllowOverwrite
	)

	dynamicparam {
		if (-not $MyInvocation.BoundParameters.ContainsKey("PackageName")) {return}

		$CopiedParams = Copy-ManifestParameters $PackageName Enable -NamePrefix "_"
		if ($null -eq $CopiedParams) {return}
		$function:ExtractParamsFn = $CopiedParams.ExtractFn
		return $CopiedParams.Parameters
	}

	begin {
		$PackagePath = Get-PackagePath $PackageName
		$ManifestPath = Get-ManifestPath $PackagePath
		$Manifest = Import-PackageManifestFile $ManifestPath

		$ForwardedParams = ExtractParamsFn $PSBoundParameters
		try {
			$PackageParameters += $ForwardedParams
		} catch {
			$CmdName = $MyInvocation.MyCommand.Name
			throw "The same parameter was passed to '${CmdName}' both using '-PackageParameters' and forwarded dynamic parameter. " +`
					"Each parameter must present in at most one of these: " + $_
		}

		if ($Manifest.ContainsKey("Private") -and $Manifest.Private) {
			echo "Enabling private package $PackageName..."
		} elseif ($Manifest.Name -eq $PackageName) {
			echo "Enabling package $($Manifest.Name), version $($Manifest.Version)..."
		} else {
			echo "Enabling package $($Manifest.Name) (installed as $PackageName), version $($Manifest.Version)..."
		}

		$InternalArgs = @{
			AllowOverwrite = [bool]$AllowOverwrite
		}

		Confirm-Manifest $Manifest
		Invoke-Container Enable $ManifestPath $PackagePath $InternalArgs $PackageParameters
		echo "Successfully enabled $PackageName."
	}
}

Export function Install- {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName,
			[Hashtable]
		$PackageParameters = @{},
			# allow overwriting current .\app directory, if one exists
			[switch]
		$AllowOverwrite,
			# download files with low priority, which results in better network
			#  responsiveness for other programs, but possibly slower download
			[switch]
		$LowPriority
	)

	dynamicparam {
		if (-not $MyInvocation.BoundParameters.ContainsKey("PackageName")) {return}

		$CopiedParams = Copy-ManifestParameters $PackageName Install -NamePrefix "_"
		if ($null -eq $CopiedParams) {return}
		$function:ExtractParamsFn = $CopiedParams.ExtractFn
		return $CopiedParams.Parameters
	}

	begin {
		$PackagePath = Get-PackagePath $PackageName
		$ManifestPath = Get-ManifestPath $PackagePath
		$Manifest = Import-PackageManifestFile $ManifestPath

		$ForwardedParams = ExtractParamsFn $PSBoundParameters
		try {
			$PackageParameters += $ForwardedParams
		} catch {
			$CmdName = $MyInvocation.MyCommand.Name
			throw "The same parameter was passed to '${CmdName}' both using '-PackageParameters' and forwarded dynamic parameter. " +`
					"Each parameter must present in at most one of these: " + $_
		}

		# Name is not required for private packages
		if ("Name" -notin $Manifest.Keys) {
			echo "Installing private package '$PackageName'..."
		} elseif ($Manifest.Name -eq $PackageName) {
			echo "Installing package '$($Manifest.Name)', version '$($Manifest.Version)'..."
		} else {
			echo "Installing package '$($Manifest.Name)' (installed as '$PackageName'), version '$($Manifest.Version)'..."
		}

		$InternalArgs = @{
			AllowOverwrite = [bool]$AllowOverwrite
			DownloadLowPriority = [bool]$LowPriority
		}

		Confirm-Manifest $Manifest
		Invoke-Container Install $ManifestPath $PackagePath $InternalArgs $PackageParameters
		echo "Successfully installed $PackageName."
	}
}

function ConfirmManifestOverwrite {
	param(
			[Parameter(Mandatory)]
			[string]
		$TargetName,
			[Parameter(Mandatory)]
			[string]
		$TargetPackageRoot,
			[Hashtable]
		$Manifest
	)

	$Title = "Overwrite existing package manifest?"
	$ManifestDescription = if ($null -eq $Manifest) {""}
			else {" (manifest '$($Manifest.Name)', version '$($Manifest.Version)')"}
	$Message = "There is already an imported package with name '$TargetName' " +`
			"in '$TargetPackageRoot'$ManifestDescription. Should we overwrite its manifest?"
	return Confirm-Action $Title $Message -ActionType "ManifestOverwrite"
}

Export function Import- {
	[CmdletBinding(PositionalBinding = $false)]
	Param(
			[Parameter(Mandatory, Position = 0)]
			[ValidateSet([RepoPackageName])]
			[string]
		$PackageName,
			# TODO: add autocomplete
			[Parameter(Position = 1)]
			[string]
		$Version = "latest",
			[string]
		$TargetName = $PackageName,
			[ValidateSet([PackageRoot])]
			[string]
		$TargetPackageRoot = $script:PACKAGE_ROOTS[0],
			[switch]
		$AllowOverwrite
	)

	if ($Version -eq "latest") {
		# find latest version
		$Version = Get-LatestPackageVersion (Join-Path $script:MANIFEST_REPO $PackageName)
	} elseif (-not (Test-Path (Join-Path $script:MANIFEST_REPO $PackageName $Version))) {
		throw "Unknown version of package '$PackageName': $Version"
	}

	Write-Verbose "Validating the manifest before importing..."
	if (-not (Confirm-PogRepositoryPackage $PackageName $Version)) {
		throw "Validation of the repository manifest failed, refusing to import."
	}

	$SrcPath = Join-Path $script:MANIFEST_REPO $PackageName $Version
	$TargetPath = Join-Path $TargetPackageRoot $TargetName

	if (Test-Path $TargetPath) {
		# target directory already exists
		# let's figure out what it contains

		$OrigManifestPath = Get-ManifestPath $TargetPath -NoError
		$OrigManifest = if ($null -eq $OrigManifestPath) {
			# it seems that there is no package manifest present
			# either a random folder was erronously created, or this is a package, but corrupted
			Write-Warning ("A directory with name '$TargetName' already exists in '$TargetPackageRoot', " +`
					"but it doesn't seem to contain a package manifest. " +`
					"All directories in a package root should be packages with a valid manifest.")
			$null
		} else {
			try {
				Import-PackageManifestFile $OrigManifestPath
			} catch {
				# package has a manifest, but it's invalid (probably corrupted)
				Write-Warning "Found an existing manifest in '$TargetName' at '$TargetPackageRoot', but it's syntactically invalid."
				$null
			}
		}

		if (-not $AllowOverwrite -and -not (ConfirmManifestOverwrite $TargetName $TargetPackageRoot $OrigManifest)) {
			throw "There is already a package with name '$TargetName' in '$TargetPackageRoot'. Pass -AllowOverwrite to overwrite current manifest without confirmation."
		}
		echo "Overwriting previous package manifest..."
		$script:MANIFEST_CLEANUP_PATHS | % {Join-Path $TargetPath $_} | ? {Test-Path $_} | rm -Recurse
	} else {
		$null = New-Item -Type Directory $TargetPath
	}

	ls $SrcPath | Copy-Item -Destination $TargetPath -Recurse
	echo "Initialized '$TargetPath' with package manifest '$PackageName' (version $Version)."
}

Export function Get-Manifest {
	[CmdletBinding()]
	Param(
			[Parameter(Mandatory)]
			[ValidateSet([RepoPackageName])]
			[string]
		$PackageName,
			# TODO: add autocomplete
			[string]
		$Version = "latest"
	)

	if ($Version -eq "latest") {
		# find latest version
		$Version = Get-LatestPackageVersion (Join-Path $script:MANIFEST_REPO $PackageName)
	} elseif (-not (Test-Path (Join-Path $script:MANIFEST_REPO $PackageName $Version))) {
		throw "Unknown version of package ${PackageName}: $Version"
	}
	return Get-ManifestPath (Join-Path $script:MANIFEST_REPO $PackageName $Version)
}

function FillManifestTemplate($PackageName, $Version) {
	$Manifest = Get-Content -Raw $RESOURCE_DIR\manifest_template.txt
	return $Manifest -f @(
		$PackageName.Replace("``", '``').Replace('"', '`"')
		$Version.Replace("``", '``').Replace('"', '`"')
	)
}

Export function New-Manifest {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[string]
		$PackageName,
			[Parameter(Mandatory)]
			[string]
		$Version
	)

	# TODO: validate state of the package directory (check if it's not empty after error,...)
	begin {
		$PackagePath = Join-Path $script:MANIFEST_REPO $PackageName
		$VersionDirPath = Join-Path $PackagePath $Version

		if (-not (Test-Path $VersionDirPath)) {
			# create manifest dir for version
			$ManifestDir = New-Item -Type Directory $VersionDirPath
		} elseif (@(ls $VersionDirPath).Count -ne 0) {
			# there is non-empty manifest dir here
			throw "Package $PackageName already has a manifest for version '$Version'."
		} else {
			# manifest dir exists, but it's empty
			$ManifestDir = Get-Item $VersionDirPath
		}

		$ManifestPath = Join-Path $ManifestDir $MANIFEST_PATHS[0]
		return New-Item -Path $ManifestPath -Value (FillManifestTemplate $PackageName $Version)
	}
}

Export function New-DirectManifest {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName
	)

	begin {
		$PackagePath = Get-PackagePath $PackageName

		$ManifestPath = try {
			Get-ManifestPath $PackagePath
		} catch {
			Resolve-VirtualPath (Join-Path $PackagePath $MANIFEST_PATHS[0])
		}

		if (Test-Path $ManifestPath) {
			throw "Package $PackageName already has a manifest at '$ManifestPath'."
		}

		return Copy-Item $RESOURCE_DIR\manifest_template_direct.psd1 $ManifestPath -PassThru
	}
}

# TODO: run this inside a container
# TODO: set working directory of the generator script to the target dir?
<# Generates manifests for new versions of the package. First checks for new versions,
    then calls the manifest generator for each version. #>
Export function Update-Manifest {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory, ValueFromPipeline)]
			[ValidateSet([RepoManifestGeneratorName])]
			[string]
		$PackageName,
			[string[]]
		$Version,
			<# Recreates even existing manifests. #>
			[switch]
		$Force
	)

	begin {
		if ($null -ne $Version -and $MyInvocation.ExpectingInput) {
			throw "$($MyInvocation.InvocationName): `$Version must not be passed when `$PackageName is passed through pipeline" +`
					" (it does not make much sense to select the same version from multiple packages)."
		}

		if ($null -ne $Version) {
			# if $Version was passed, overwrite even existing manifest
			$Force = $true
		}
	}

	process {
		$GenDir = Join-Path $script:MANIFEST_GENERATOR_REPO $PackageName
		$ManifestDir = Join-Path $script:MANIFEST_REPO $PackageName

		if (-not (Test-Path $ManifestDir)) {
			$null = mkdir $ManifestDir
		}

		try {
			# list available versions without existing manifest (unless -Force is set, then all versions are listed)
			# only generate manifests for versions that don't already exist, unless -Force is passed
			$ExistingVersions = ls -Directory $ManifestDir | % {$_.Name}
			$GeneratedVersions = & "$GenDir\versions.ps1"
				| % {
					# apparently, we have to use the full name of pscustomobject, because using the shorthand name does weird stuff
					#  related issue: https://github.com/PowerShell/PowerShell/issues/16361
					#  example of the weird behavior:
					#   True: (echo 1) -is [pscustomobject]
					#   False: ([pscustomobject]@{}).GetType() -eq [pscustomobject]
					#  for psobject and hashtable, everything seems to work as expected
					# TODO: this deserves automated tests, I don't particularly trust that it's correct, although it worked so far
					# TODO: move the version retrieval to separate function to allow testing

					$IsMap = $_ -is [System.Management.Automation.PSCustomObject] `
							-or $_ -is [hashtable]`
							-or $_.GetType() -eq [psobject]
					$Obj = $_
					# VersionStr to not shadow Version parameter
					$VersionStr = if (-not $IsMap) {$_} else {
						try {$_.Version}
						catch {throw "Version generator for package '$PackageName' returned a custom object without a Version property: $Obj" +`
								"  (Version generators must return either a version string, or a" +`
								" map container (hashtable, psobject, pscustomobject) with a Version property.)"}
					}
					return [pscustomobject]@{
						Version = [string]$VersionStr
						# store the original value, so that we can pass it unchanged to the manifest generator
						OriginalValue = $_
					}
				}
				| ? {
					if ([string]::IsNullOrEmpty($_)) {
						throw "Empty package version generated by the version generator for package '$PackageName' (either `$null or empty string)."
					}
					$true
				}
				| ? {$Force -or $_.Version -notin $ExistingVersions}
				# if $Version was passed, filter out the versions; as the versions generated
				#  by the script may have other metadata, we cannot use the versions passed in $Version directly
				| ? {$null -eq $Version -or $_.Version -in $Version}


			if ($null -ne $Version -and @($Version).Count -ne @($GeneratedVersions).Count) {
				$FoundVersions = $GeneratedVersions | % {$_.Version}
				$MissingVersionsStr = $Version | ? {$_ -notin $FoundVersions} | Join-String -Separator ", "
				throw "Some of the package versions passed in `$Version were not found for package '$PackageName': $MissingVersionsStr" +`
						"  (Are you sure these versions exist?)"
			}


			# generate manifest for each version
			$GeneratedVersions | % {
				$TargetDir = Join-Path $ManifestDir $_.Version
				if (-not (Test-Path $TargetDir)) {$null = mkdir $TargetDir}
				try {
					Write-Information "Generating manifest '$PackageName', version '$($_.Version)'..."
					$null = & "$GenDir\generator.ps1" $_.OriginalValue $TargetDir
					if (-not (ls $TargetDir | select -First 1)) {
						# target dir is empty, nothing was generated
						rm $TargetDir
						# TODO: also check if the manifest file itself is generated, or possibly run full validation (Confirm-RepositoryPackage)
						throw "Manifest generator for package '$PackageName', version '$($_.Version) did not generate any files."
					}
				} catch {
					# generator failed
					rm -Recurse $TargetDir
					throw
				}
			}
		} finally {
			# test if dir is empty; this only reads the first entry, avoids listing the whole dir
			if (-not (ls $ManifestDir | select -First 1)) {
				rm -Recurse $ManifestDir
			}
		}
	}
}

Export function Confirm-RepositoryPackage {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)]
			[ValidateSet([RepoPackageName])]
			[string]
		$PackageName,
			[Parameter(ValueFromPipelineByPropertyName)]
			[string]
		$Version
	)

	begin {
		# rename to avoid shadowing
		$VersionParam = $Version

		$NoIssues = $true
		function AddIssue($IssueMsg) {
			Set-Variable NoIssues $false -Scope 1
			Write-Warning $IssueMsg
		}
	}

	process {
		$VersionStr = if ($VersionParam) {", version '$VersionParam'"} else {" (all versions)"}
		Write-Verbose "Validating package '$PackageName'$VersionStr from local repository..."

		if (-not $VersionParam) {
			$DirPath = Join-Path $script:MANIFEST_REPO $PackageName
			if (ls -File $DirPath) {
				AddIssue "Package '$PackageName' has incorrect structure; root contains following files (only version directories should be present): $Files"
				return
			}
			$VersionDirs = ls -Directory $DirPath
		} else {
			$DirPath = Join-Path $script:MANIFEST_REPO $PackageName $VersionParam
			if (-not (Test-Path $DirPath)) {
				throw "Could not find version '$VersionParam' of package '$PackageName' in the local repository. Tested path: '$DirPath'"
			}
			$VersionDirs = Get-Item $DirPath
		}

		$VersionDirs | % {
			$Version = $_.Name

			if (@(ls $_).Count -gt 1) {
				AddIssue ("In the root of each package manifest directory should be either a single 'pog.psd1' file, " `
						+ "or a '.pog' directory containing a 'pog.psd1' file and other support files or directories. " `
						+ "Instead, multiple files or directories were found for version '$Version' of package '$PackageName'.")
			}

			try {
				$ManifestPath = Get-ManifestPath $_
			} catch {
				AddIssue "Could not find manifest for version '$Version' of package '$PackageName': $_"
				return
			}

			try {
				$Manifest = Import-PackageManifestFile $ManifestPath
			} catch {
				AddIssue $_
				return
			}

			try {
				Confirm-Manifest $Manifest $PackageName $Version
			} catch {
				AddIssue "Validation of package manifest '$PackageName', version '$Version' from local repository failed: $_"
			}
		}
	}

	end {
		return $NoIssues
	}
}

# TODO: expand to really check whole package, not just manifest
Export function Confirm-Package {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory, ValueFromPipeline)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName
	)

	process {
		$PackagePath = Get-PackagePath $PackageName
		$ManifestPath = Get-ManifestPath $PackagePath

		try {
			$Manifest = Import-PackageManifestFile $ManifestPath
		} catch {
			Write-Warning $_
			return
		}

		Write-Verbose "Validating imported package manifest '$PackageName' at '$ManifestPath'..."
		try {
			Confirm-Manifest $Manifest
		} catch {
			Write-Warning "Validation of imported package manifest '$PackageName' at '$ManifestPath' failed: $_"
			return
		}
	}
}