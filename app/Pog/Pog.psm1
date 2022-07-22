# Requires -Version 7
using module .\Paths.psm1
using module .\lib\Utils.psm1
using module .\Common.psm1
using module .\Confirmations.psm1
. $PSScriptRoot\lib\header.ps1

# TODO: allow wildcards in PackageName and Version arguments where it makes sense
# TODO: allow pipelining for Import-, Install- and Enable-
#  (careful about overwriting parameters based on values of other parameters,
#   e.g. `Get-PogRepositoryPackage git, 7zip -LatestVersion | Import-Pog` will attempt to write 7zip manifest to `git` target)


class ImportedPackageName : System.Management.Automation.IValidateSetValuesGenerator {
	[String[]] GetValidValues() {
		return ls $script:PATH_CONFIG.PackageRoots.ValidPackageRoots -Directory | select -ExpandProperty Name
	}
}

class RepoPackageName : System.Management.Automation.IValidateSetValuesGenerator {
	[String[]] GetValidValues() {
		return $script:REPOSITORY.EnumeratePackageNames();
	}
}

class RepoManifestGeneratorName : System.Management.Automation.IValidateSetValuesGenerator {
	[String[]] GetValidValues() {
		return ls $script:PATH_CONFIG.ManifestGeneratorDir -Directory | select -ExpandProperty Name
	}
}

class PackageRoot : System.Management.Automation.IValidateSetValuesGenerator {
	[String[]] GetValidValues() {
		return $script:PATH_CONFIG.PackageRoots.AllPackageRoots
	}
}

function NewPackageVersion($v) {
	return [Pog.PackageVersion]::new($v)
}

# whew, that's a lot of types...
class ManifestVersionCompleter : System.Management.Automation.IArgumentCompleter {
	[System.Collections.Generic.IEnumerable[System.Management.Automation.CompletionResult]]
	CompleteArgument(
			[string]$CommandName, [string]$ParameterName, [string]$WordToComplete,
			[System.Management.Automation.Language.CommandAst]$Ast,
			[System.Collections.IDictionary]$BoundParameters
	) {
		$ResultList = [System.Collections.Generic.List[System.Management.Automation.CompletionResult]]::new()
		if (-not $BoundParameters.ContainsKey("PackageName")) {
			return $ResultList
		} elseif (@($BoundParameters.PackageName).Count -gt 1) {
			return $ResultList # cannot set version when multiple package names are specified
		}

		$c = $script:REPOSITORY.GetPackage($BoundParameters.PackageName, $false)
		if (-not $c.Exists) {
			return $ResultList # no such package
		}

		$c.EnumerateVersionStrings() | ? {$_.StartsWith($WordToComplete)} |
				% {NewPackageVersion $_} | sort -Descending | % {$ResultList.Add($_.ToString())}
		return $ResultList
	}
}


function Export-AppShortcut {
	param(
		[Parameter(Mandatory)]$AppPath,
		[Parameter(Mandatory)]$ExportPath
	)

	$Shortcuts = ls -File -Filter "*.lnk" $AppPath
	$Shortcuts | % {
		Copy-Item $_ -Destination $ExportPath
		Write-Verbose "Exported shortcut '$($_.Name)' from '$(Split-Path -Leaf $AppPath)'."
	}
	return @($Shortcuts).Count
}


Export function Export-ShortcutsToStartMenu {
	[CmdletBinding()]
	param(
			[switch]
		$UseSystemWideMenu
	)

	$TargetDir = if ($UseSystemWideMenu) {
		[Pog.PathConfig]::StartMenuSystemExportDir
	} else {
		[Pog.PathConfig]::StartMenuUserExportDir
	}

	if (Test-Path $TargetDir) {
		Write-Information "Clearing previous Pog start menu entries..."
		Remove-Item -Recurse $TargetDir
	}

	Write-Information "Exporting shortcuts to '$TargetDir'."
	$null = New-Item -ItemType Directory $TargetDir

	$ShortcutCount = ls $PATH_CONFIG.PackageRoots.ValidPackageRoots -Directory `
		| % {Export-AppShortcut $_.FullName $TargetDir} `
		| Measure-Object -Sum | % Sum
	Write-Information "Exported $ShortcutCount shortcuts."
}


Export function Get-RepositoryPackage {
	[CmdletBinding(DefaultParameterSetName="Version")]
	[OutputType([Pog.RepositoryPackage])]
	param(
			[Parameter(Position=0)]
			[ValidateSet([RepoPackageName])]
			[string[]]
		$PackageName,
			# TODO: figure out how to remove this parameter when -PackageName is an array
			[Parameter(Position=1, ParameterSetName="Version")]
			[ArgumentCompleter([ManifestVersionCompleter])]
			[Pog.PackageVersion]
		$Version,
			[Parameter(ParameterSetName="LatestVersion")]
			[switch]
		$LatestVersion
	)

	if ($Version) {
		if (-not $PackageName) {throw "-Version must not be passed without also passing -PackageName."}
		if (@($PackageName).Count -gt 1) {throw "-Version must not be passed when -PackageName contains multiple package names."}
	}

	if ($PackageName -and $Version) {
		$p = $REPOSITORY.GetPackage($PackageName, $true).GetVersion($Version)
		if (-not $p.Exists) {
			throw "Package '$PackageName' does not have version '$($p.Version)'."
		}
		return $p
	}

	if (-not $PackageName) {
		[string[]]$PackageName = $REPOSITORY.EnumeratePackageNames()
	}
	foreach ($p in $PackageName) {
		$c = $REPOSITORY.GetPackage($p, $true)
		if ($LatestVersion) {
			echo $c.GetLatestPackage()
		} else {
			echo $c.EnumerateSorted()
		}
	}
}

Export function Get-Package {
	[CmdletBinding()]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(ValueFromPipeline, ValueFromPipelineByPropertyName)]
			[ValidateSet([ImportedPackageName])]
			[string[]]
		$PackageName = [ImportedPackageName]::new().GetValidValues()
	)

	process {
		foreach ($p in $PackageName) {
			# do not eagerly load the manifest ---\/
			$PACKAGE_ROOTS.GetPackage($p, $true, $false)
		}
	}
}

Export function Get-Root {
	return $PATH_CONFIG.PackageRoots.AllPackageRoots
}

# functions to programmatically add/remove package roots are intentionally not provided, because it is a bit non-trivial
#  to get the file updates right from a concurrency perspective
# TODO: ^ figure out how to provide the functions safely
Export function Edit-RootList {
	$Path = $PATH_CONFIG.PackageRoots.PackageRootFile
	Write-Information "Opening the package root list at '$Path' for editing in a text editor..."
	Write-Information "Each line should contain a single absolute path to the package root directory."
	# this opens the file for editing in a text editor (it's a .txt file)
	Start-Process $Path
}

<# Remove cached package archives older than the provided date. #>
Export function Clear-DownloadCache {
	[CmdletBinding(DefaultParameterSetName = "Days")]
	param(
			[Parameter(Mandatory, ParameterSetName = "Date", Position = 0)]
			[DateTime]
		$DateBefore,
			[Parameter(ParameterSetName = "Days", Position = 0)]
			[int]
		$DaysBefore = 0,
			[switch]
		$Force
	)

	if ($PSCmdlet.ParameterSetName -eq "Days") {
		$DateBefore = [DateTime]::Now.AddDays(-$DaysBefore)
	}

	$SizeSum = 0
	$CacheEntries = ls -Directory $PATH_CONFIG.DownloadCacheDir | % {
		$SourceFile = Get-Item "$_.json" -ErrorAction Ignore
		$LastAccessTime = if ($SourceFile) {$SourceFile.LastWriteTime} else {$_.LastWriteTime}

		if ($LastAccessTime -gt $DateBefore) {
			return # recently used entry, don't delete
		}

		# read cache entry sources from the metadata file
		$SourceStr = try {
			Get-Content -Raw $SourceFile | ConvertFrom-Json -AsHashtable | % {
				$_.PackageName + $(if ($_.ManifestVersion) {" $($_.ManifestVersion)"})
			} | Join-String -Separator ", "
		} catch {$null}

		$CachedFile = ls -File $_
		$SizeSum += $CachedFile.Length
		return @{
			EntryDirectory = $_
			SourceFile = $SourceFile
			SourceStr = $SourceStr
			EntryName = $CachedFile.Name
			EntrySize = $CachedFile.Length
		}
	}

	function RemoveEntries {
		$CacheEntries | % {$_.EntryDirectory; $_.SourceFile} | ? {Test-Path $_} | Remove-Item -Recurse -Force
	}


	if (@($CacheEntries).Count -eq 0) {
		if ($Force) {
			Write-Information "No package archives older than '$($DateBefore.ToString())' found, nothing to remove."
			return
		} else {
			throw "No cached package archives downloaded before '$($DateBefore.ToString())' found."
		}
	}

	if ($Force) {
		# do not check for confirmation
		RemoveEntries
		$C = @($CacheEntries).Count
		Write-Information ("Removed $C package archive" + $(if ($C -eq 1) {""} else {"s"}) +`
						   ", freeing ~{0:F} GB of space." -f ($SizeSum / 1GB))
		return
	}

	# print the cache entry list
	$CacheEntries | sort EntrySize -Descending | % {
		Write-Host ("{0,10:F2} MB - {1}" -f @(
			($_.EntrySize / 1MB),
			$(if ($_.SourceStr) {$_.SourceStr} else {$_.EntryName})))
	}

	$Title = "Remove the listed package archives, freeing ~{0:F} GB of space?" -f ($SizeSum / 1GB)
	$Message = "This will not affect installed applications. Reinstallation of an application may take longer," + `
		" as it will have to be downloaded again."
	if (Confirm-Action $Title $Message) {
		RemoveEntries
	} else {
		Write-Host "No package archives were removed."
	}
}


function GetPackageDescriptionStr($PackageName, $Manifest) {
	$VersionStr = if ($Manifest.Version) {", version '$($Manifest.Version)'"} else {""}
	if ($Manifest.IsPrivate) {
		return "private package '$PackageName'$VersionStr"
	} elseif ($Manifest.Name -eq $PackageName) {
		return "package '$($Manifest.Name)'$VersionStr"
	} else {
		return "package '$($Manifest.Name)' (installed as '$PackageName')$VersionStr"
	}
}

Export function Enable- {
	# .SYNOPSIS
	#	Enables an installed package to allow external usage.
	# .DESCRIPTION
	#	Enables an installed package, setting up required files and exporting public commands and shortcuts.
	[CmdletBinding()]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName,
			### Extra parameters to pass to the Enable script in the package manifest. For interactive usage,
			### prefer to use the automatically generated parameters on this command (e.g. instead of passing
			### `@{Arg = Value}` to this parameter, pass `-_Arg Value` as a standard parameter to this cmdlet),
			### which gives you autocomplete and name/type checking.
			[Hashtable]
		$PackageParameters = @{},
			# allows overriding existing commands without confirmation
			[switch]
		$Force,
			<# Return a [Pog.ImportedPackage] object with information about the enabled package. #>
			[switch]
		$PassThru
	)

	dynamicparam {
		if (-not $PSBoundParameters.ContainsKey("PackageName")) {return}
		# this may fail in case the manifest is invalid, don't throw here, just return, will be handled in the begin{} block
		$p = try {$PACKAGE_ROOTS.GetPackage($PackageName, $true, $true)} catch {return}

		$CopiedParams = Copy-ManifestParameters $p.Manifest Enable -NamePrefix "_"
		$function:ExtractParamsFn = $CopiedParams.ExtractFn
		return $CopiedParams.Parameters
	}

	begin {
		if (Test-Path Function:ExtractParamsFn) {
			# $p and $Manifest are already loaded
			$ForwardedParams = ExtractParamsFn $PSBoundParameters
			try {
				$PackageParameters += $ForwardedParams
			} catch {
				$CmdName = $MyInvocation.MyCommand.Name
				throw "The same parameter was passed to '${CmdName}' both using '-PackageParameters' and forwarded dynamic parameter. " +`
						"Each parameter must be present in at most one of these: " + $_
			}
		} else {
			Write-Debug "Loading manifest inside the begin{} block, dynamicparam failed"
			# this will throw in case the package or the manifest are not valid
			$p = $PACKAGE_ROOTS.GetPackage($PackageName, $true, $true)
		}

		Confirm-Manifest $p.Manifest

		if (-not $p.Manifest.Raw.ContainsKey("Enable")) {
			Write-Information "Package '$($p.PackageName)' does not have an Enable block."
			return
		}

		$InternalArgs = @{
			AllowOverwrite = [bool]$Force
		}

		Write-Information "Enabling $(GetPackageDescriptionStr $p.PackageName $p.Manifest)..."
		Invoke-Container Enable $p -Manifest $p.Manifest -InternalArguments $InternalArgs -PackageArguments $PackageParameters
		Write-Information "Successfully enabled $($p.PackageName)."
		if ($PassThru) {
			return $p
		}
	}
}

Export function Install- {
	# .SYNOPSIS
	#	Downloads and extracts package files.
	# .DESCRIPTION
	#	Downloads and extracts package files, populating the ./app directory of the package. Downloaded files
	#	are cached, so repeated installs only require internet connection for the initial download.
	[CmdletBinding()]
	[OutputType([Pog.ImportedPackage])]
	param(
			### Name of the package to install. This is the install name, not necessarily the manifest app name.
			[Parameter(Mandatory)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName,
			### If set, and some version of the package is already installed, prompt before overwriting
			### with the current version according to the manifest.
			[switch]
		$Confirm,
			### If set, files are downloaded with low priority, which results in better network responsiveness
			### for other programs, but possibly slower download speed.
			[switch]
		$LowPriority,
			<# Return a [Pog.ImportedPackage] object with information about the installed package. #>
			[switch]
		$PassThru
	)

	begin {
		$p = $PACKAGE_ROOTS.GetPackage($PackageName, $true, $true)

		Confirm-Manifest $p.Manifest

		if (-not $p.Manifest.Raw.ContainsKey("Install")) {
			Write-Information "Package '$($p.PackageName)' does not have an Install block."
			return
		}

		$InternalArgs = @{
			AllowOverwrite = -not [bool]$Confirm
			DownloadLowPriority = [bool]$LowPriority
		}

		Write-Information "Installing $(GetPackageDescriptionStr $p.PackageName $p.Manifest)..."
		Invoke-Container Install $p -Manifest $p.Manifest -InternalArguments $InternalArgs
		Write-Information "Successfully installed $($p.PackageName)."
		if ($PassThru) {
			return $p
		}
	}
}

function ClearPreviousPackageDir($p, $TargetPackageRoot, $SrcPackage, [switch]$Force) {
	$OrigManifest = $null
	try {
		# try to load the (possibly) existing manifest
		$p.ReloadManifest()
		$OrigManifest = $p.Manifest
	} catch [System.IO.DirectoryNotFoundException] {
		# the package does not exist, create the directory and return
		$null = New-Item -Type Directory $p.Path
		return
	} catch [Pog.PackageManifestNotFoundException] {
		# the package exists, but the manifest is missing
		# either a random folder was erronously created, or this is a package, but corrupted
		Write-Warning ("A directory with name '$($p.PackageName)' already exists in '$TargetPackageRoot'," `
				+ " but it doesn't seem to contain a package manifest." `
				+ " All directories in a package root should be packages with a valid manifest.")
	} catch [Pog.PackageManifestParseException] {
		# the package has a manifest, but it's invalid (probably corrupted)
		Write-Warning "Found an existing manifest in '$($p.PackageName)' at '$TargetPackageRoot', but it's syntactically invalid."
	}

	if (-not $Force) {
		# prompt for confirmation
		$Title = "Overwrite existing package manifest?"
		$ManifestDescription = if ($null -eq $OrigManifest) {""}
				else {" (manifest '$($OrigManifest.Name)', version '$($OrigManifest.Version)')"}
		$Message = "There is already an imported package with name '$($p.PackageName)'" `
				+ " in '$TargetPackageRoot'$ManifestDescription. Overwrite its manifest with version '$($SrcPackage.Version)'?"
		if (-not (Confirm-Action $Title $Message -ActionType "ManifestOverwrite")) {
			throw "There is already a package with name '$($p.PackageName)' in '$TargetPackageRoot'." `
				+ " Pass -Force to overwrite the current manifest without confirmation."
		}
	}

	Write-Information "Overwriting previous package manifest..."
	[Pog.PathConfig]::PackageManifestCleanupPaths | % {Join-Path $TargetPath $_} | ? {Test-Path $_} | Remove-Item -Recurse
}

Export function Import- {
	# .SYNOPSIS
	#	Imports a package manifest from the repository.
	[CmdletBinding(PositionalBinding = $false)]
	[OutputType([Pog.ImportedPackage])]
	Param(
			[Parameter(Mandatory, Position = 0)]
			[ValidateSet([RepoPackageName])]
			[string]
		$PackageName,
			[Parameter(Position = 1)]
			[ArgumentCompleter([ManifestVersionCompleter])]
			[Pog.PackageVersion]
		$Version,
			[string]
		$TargetName,
			[ValidateSet([PackageRoot])]
			[string]
			# FIXME: we should resolve the package root path to the correct casing
		$TargetPackageRoot = $PATH_CONFIG.PackageRoots.ValidPackageRoots[0],
			<# Overwrite an existing package without prompting for confirmation. #>
			[switch]
		$Force,
			<# Return a [Pog.ImportedPackage] object with information about the imported package. #>
			[switch]
		$PassThru
	)

	begin {
		$c = $REPOSITORY.GetPackage($PackageName, $true)
		# get the resolved name, so that the casing is correct
		$PackageName = $c.PackageName

		if (-not $TargetName) {
			# this must be done after the $PackageName update above
			$TargetName = $PackageName
		}

		if (-not $Version) {
			# find latest version
			$SrcPackage = $c.GetLatestPackage()
		} else {
			$SrcPackage = $c.GetVersion($Version)
			if (-not $SrcPackage.Exists) {
				throw "Unknown version of package '$PackageName': $Version"
			}
		}

		Write-Verbose "Validating the repository package before importing..."
		# this forces a manifest load on $SrcPackage
		if (-not (Confirm-RepositoryPackage $SrcPackage)) {
			throw "Validation of the repository package failed (see warnings above), not importing."
		}

		# don't load the manifest yet (may not be valid, will be loaded in ClearPreviousPackageDir)
		$p = $PACKAGE_ROOTS.GetPackage($TargetName, $TargetPackageRoot, $true, $false)

		# ensure $TargetPath exists and there's no package manifest
		ClearPreviousPackageDir $p $TargetPackageRoot $SrcPackage -Force:$Force

		ls $SrcPackage.Path | Copy-Item -Recurse -Destination $p.Path
		Write-Information "Initialized '$($p.Path)' with package manifest '$PackageName' (version '$($SrcPackage.Version)')."
		if ($PassThru) {
			# reload to remove the previous cached manifest
			# the imported manifest was validated, this should not throw
			$p.ReloadManifest()
			return $p
		}
	}
}

Export function Get-ManifestHash {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)]
			[ValidateSet([RepoPackageName])]
			[string]
		$PackageName,
			[Parameter(ValueFromPipelineByPropertyName)]
			[ArgumentCompleter([ManifestVersionCompleter])]
			[Pog.PackageVersion]
		$Version,
			### If set, files are downloaded with low priority, which results in better network responsiveness
			### for other programs, but possibly slower download speed.
			[switch]
		$LowPriority
	)
	# TODO: -PassThru to return structured object instead of Write-Host pretty-print

	process {
		$c = $REPOSITORY.GetPackage($PackageName, $true)
		# get the resolved name, so that the casing is correct
		$PackageName = $c.PackageName

		if (-not $Version) {
			# find latest version
			$p = $c.GetLatestPackage()
		} else {
			$p = $c.GetVersion($Version)
			if (-not $p.Exists) {
				throw "Unknown version of package '$($p.PackageName)': $($p.Version)"
			}
		}

        # this forces a manifest load on $p
		if (-not (Confirm-RepositoryPackage $p)) {
			throw "Validation of the repository package failed (see warnings above)."
		}

		$InternalArgs = @{
			AllowOverwrite = $true # not used
			DownloadLowPriority = [bool]$LowPriority
		}

		Invoke-Container GetInstallHash $p -Manifest $p.Manifest -InternalArguments $InternalArgs
	}
}

Export function New-Manifest {
	[CmdletBinding()]
	[OutputType([Pog.RepositoryPackage])]
	param(
			[Parameter(Mandatory)]
			[ValidateScript({
				$InvalidI = $_.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars())
				if ($InvalidI -ge 0) {throw "Must be a valid directory name, cannot contain invalid characters like '$($_[$InvalidI])': $_"}
				if ($_ -eq "." -or $_ -eq "..") {throw "Must be a valid directory name, not '.' or '..': $_"}
				return $true
			})]
			[string]
		$PackageName,
			[Parameter(Mandatory)]
			[Pog.PackageVersion]
		$Version
	)

	begin {
		$p = $REPOSITORY.GetPackage($PackageName, $true).GetVersion($Version)

		if (-not $p.Exists) {
			# create manifest dir for version
			$null = New-Item -Type Directory $p.Path
		} elseif (@(ls $p.Path).Count -ne 0) {
			# there is non-empty package dir here
			# TODO: validate state of the package directory (check if it's not empty after error,...)
			throw "Package $($p.PackageName) already has a manifest for version '$($p.Version)'."
		}

		$Template = Get-Content -Raw "$PSScriptRoot\resources\repository_manifest_template.psd1"
		$Template = $Template.Replace("'{{NAME}}'", "'" + $p.PackageName.Replace("'", "''") + "'")
		$Template = $Template.Replace("'{{VERSION}}'", "'" + $p.Version.ToString().Replace("'", "''") + "'")
		$null = New-Item -Path $p.ManifestPath -Value $Template
		return $p
	}
}

Export function New-DirectManifest {
	[CmdletBinding()]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName
	)

	begin {
		$p = $PACKAGE_ROOTS.GetPackage($PackageName, $true, $false)

		if (Test-Path $p.ManifestPath) {
			throw "Package $($p.PackageName) already has a manifest at '$($p.ManifestPath)'."
		}

		Copy-Item $PSScriptRoot\resources\direct_manifest_template.psd1 $p.ManifestPath
		return $p
	}
}

<# Retrieve all existing versions of a package by calling the package version generator script. #>
function RetrievePackageVersions($PackageName, $GenDir) {
	return & "$GenDir\versions.ps1" | % {
			# the returned object should either be the version string directly, or a map object
			#  (hashtable/pscustomobject/psobject) that has the Version property
			#  why not use -is? that's why: https://github.com/PowerShell/PowerShell/issues/16361
			$IsMap = $_.PSTypeNames[0] -in @("System.Collections.Hashtable", "System.Management.Automation.PSCustomObject")
			$Obj = $_
			# VersionStr to not shadow Version parameter
			$VersionStr = if (-not $IsMap) {$_} else {
				try {$_.Version}
				catch {throw "Version generator for package '$PackageName' returned a custom object without a Version property: $Obj" +`
						"  (Version generators must return either a version string, or a" +`
						" map container (hashtable, psobject, pscustomobject) with a Version property.)"}
			}

			if ([string]::IsNullOrEmpty($VersionStr)) {
				throw "Empty package version generated by the version generator for package '$PackageName' (either `$null or empty string)."
			}

			return [pscustomobject]@{
				Version = [Pog.PackageVersion]$VersionStr
				# store the original value, so that we can pass it unchanged to the manifest generator
				OriginalValue = $_
			}
		}
}

# TODO: run this inside a container
# TODO: set working directory of the generator script to the target dir?
<# Generates manifests for new versions of the package. First checks for new versions,
   then calls the manifest generator for each version. #>
Export function Update-Manifest {
	[CmdletBinding()]
	[OutputType([Pog.RepositoryPackage])]
	param(
			[Parameter(ValueFromPipeline)]
			[ValidateSet([RepoManifestGeneratorName])]
			[string]
		$PackageName,
			# we only use -Version to match against retrieved versions, no need to parse
			[string[]]
		$Version,
			<# Recreates even existing manifests. #>
			[switch]
		$Force,
			<# Only retrieve and list versions, do not generate manifests. #>
			[switch]
		$ListOnly
	)

	begin {
		if ([string]::IsNullOrEmpty($PackageName) -and -not $MyInvocation.ExpectingInput) {
			if ($null -ne $Version) {
				throw "$($MyInvocation.InvocationName): `$Version must not be passed when `$PackageName is not set" +`
						" (it does not make much sense to select the same version for all generated packages)."
			}
			$GeneratorNames = [RepoManifestGeneratorName]::new().GetValidValues()
			$i = 0
			$c = @($GeneratorNames).Count
			$GeneratorNames | % {
				Write-Progress -Activity "Updating manifests" -PercentComplete (100 * ($i/$c)) -Status "$($i+1)/${c} Updating '$_'..."
				$i++
				return $_
				# call itself with all existing manifest generators
			} | & $MyInvocation.MyCommand @PSBoundParameters
			Write-Progress -Activity "Updating manifests" -Completed
			return
		}

		if ($Version -and $MyInvocation.ExpectingInput) {
			throw "$($MyInvocation.InvocationName): `$Version must not be passed when `$PackageName is passed through pipeline" +`
					" (it does not make much sense to select the same version from multiple packages)."
		}

		if ($Version) {
			# if $Version was passed, overwrite even existing manifests
			$Force = $true
		}
	}

	process {
		if ([string]::IsNullOrEmpty($PackageName)) {
			return # ignore, already processed in begin block
		}

		$c = $REPOSITORY.GetPackage($PackageName, $true)
		$PackageName = $c.PackageName
		$GenDir = Join-Path $PATH_CONFIG.ManifestGeneratorDir $PackageName

		if (-not $c.Exists) {
			$null = New-Item -Type Directory $c.Path
		}

		try {
			# list available versions without existing manifest (unless -Force is set, then all versions are listed)
			# only generate manifests for versions that don't already exist, unless -Force is passed
			$ExistingVersions = $c.EnumerateVersionStrings()
			$GeneratedVersions = RetrievePackageVersions $PackageName $GenDir
				# if -Force was not passed, filter out versions with already existing manifest
				| ? {$Force -or $_.Version -notin $ExistingVersions}
				# if $Version was passed, filter out the versions; as the versions generated by the script
				#  may have other metadata, we cannot use the versions passed in $Version directly
				| ? {-not $Version -or $_.Version -in $Version}

			if ($Version -and @($Version).Count -ne @($GeneratedVersions).Count) {
				$FoundVersions = $GeneratedVersions | % {$_.Version}
				$MissingVersionsStr = $Version | ? {$_ -notin $FoundVersions} | Join-String -Separator ", "
				throw "Some of the package versions passed in -Version were not found for package '$PackageName': $MissingVersionsStr" +`
						"  (Are you sure these versions exist?)"
			}

			if ($ListOnly) {
				# useful for testing if all expected versions are retrieved
				return $GeneratedVersions | % {$c.GetVersion($_.Version)}
			}

			# generate manifest for each version
			$GeneratedVersions | % {
				$p = $c.GetVersion($_.Version)
				if (-not $p.Exists) {
					$null = New-Item -Type Directory $p.Path
				}
				try {
					$null = & "$GenDir\generator.ps1" $_.OriginalValue $p.Path
					if (ls $p.Path | select -First 1) {
						echo $p
					} else {
						# target dir is empty, nothing was generated
						rm $p.Path
						# TODO: also check if the manifest file itself is generated, or possibly run full validation (Confirm-RepositoryPackage)
						throw "Manifest generator for package '$($p.PackageName)', version '$($p.Version) did not generate any files."
					}
				} catch {
					# generator failed
					rm -Recurse $p.Path
					throw
				}
			}
		} finally {
			# test if dir is empty; this only reads the first entry, avoids listing the whole dir
			if (-not (ls $c.Path | select -First 1)) {
				rm -Recurse $c.Path
			}
		}
	}
}

Export function Confirm-RepositoryPackage {
	[CmdletBinding(DefaultParameterSetName="Separate")]
	param(
			[Parameter(Mandatory, Position=0, ValueFromPipeline, ValueFromPipelineByPropertyName, ParameterSetName="Separate")]
			[ValidateSet([RepoPackageName])]
			[string]
		$PackageName,
			[Parameter(Position=1, ValueFromPipelineByPropertyName, ParameterSetName="Separate")]
			[ArgumentCompleter([ManifestVersionCompleter])]
			[Pog.PackageVersion]
		$Version,
			[Parameter(Mandatory, Position=0, ParameterSetName="Package")]
			[Pog.RepositoryPackage]
		$Package
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
		if ($Package) {
			$VersionPackages = $Package
		} else {
			$c = $REPOSITORY.GetPackage($PackageName, $true)
			$VersionStr = if ($VersionParam) {", version '$VersionParam'"} else {" (all versions)"}
			Write-Verbose "Validating package '$PackageName'$VersionStr from local repository..."

			$VersionPackages = if (-not $VersionParam) {
				$Files = ls -File $c.Path
				if ($Files) {
					AddIssue "Package '$PackageName' has incorrect structure; root contains following files (only version directories should be present): $Files"
					return
				}
				$c.EnumerateSorted()
			} else {
				$p = $c.GetVersion($VersionParam)
				if (-not $p.Exists) {
					throw "Could not find version '$VersionParam' of package '$PackageName' in the local repository. Tested path: '$($p.Path)'"
				}
				$p
			}
		}

		foreach ($p in $VersionPackages) {
			foreach ($f in ls $p.Path) {
				if ($f.Name -notin "pog.psd1", ".pog") {
					AddIssue ("Manifest directory for '$PackageName', version '$($p.Version)' contains extra file/directory '$($f.Name)' at '$f'." `
							+ " Each manifest directory must only contain a pog.psd1 manifest file and an optional .pog directory for extra files.")
				}
			}

			$ExtraFileDir = Get-Item "$p\.pog" -ErrorAction Ignore
			if ($ExtraFileDir -and $ExtraFileDir -isnot [System.IO.DirectoryInfo]) {
				AddIssue ("'$ExtraFileDir' should be a directory, not a file, in manifest directory for '$PackageName', version '$($p.Version)'.")
			}

			if (-not $p.ManifestExists) {
				AddIssue "Could not find manifest '$PackageName', version '$($p.Version)'. Searched path: $($p.ManifestPath)"
				return
			}

			try {
				$p.ReloadManifest()
			} catch {
				AddIssue $_
				return
			}

			try {
				Confirm-Manifest $p.Manifest $PackageName $p.Version -IsRepositoryManifest
			} catch {
				AddIssue ("Validation of package manifest '$PackageName', version '$($p.Version)' from local repository failed." +`
						"`n    Path: $ManifestPath" +`
						"`n" +`
						"`n    " + $_.ToString().Replace("`t", "     - "))
			}
		}
	}

	end {
		return $NoIssues
	}
}

# TODO: expand to really check whole package, not just manifest, then update Install- and Enable- to use this instead of Confirm-Manifest
#  when switched, figure out what to do about the forced manifest reload (we do not want to load the manifest multiple times)
Export function Confirm-Package {
	[CmdletBinding(DefaultParameterSetName="Name")]
	param(
			[Parameter(Mandatory, Position=0, ValueFromPipeline, ParameterSetName="Name")]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName,
			[Parameter(Mandatory, Position=0, ValueFromPipeline, ParameterSetName="Package")]
			[Pog.ImportedPackage]
		$Package
	)

	begin {
		$NoIssues = $true
		function AddIssue($IssueMsg) {
			Set-Variable NoIssues $false -Scope 1
			Write-Warning $IssueMsg
		}
	}

	process {
		$p = if ($Package) {
			try {
				# re-read manifest to have it up-to-date and revalidated
				$p.ReloadManifest()
			} catch {
				Write-Warning $_
				return
			}
			$Package
		} else {
			try {
				$PACKAGE_ROOTS.GetPackage($PackageName, $true, $true)
			} catch {
				Write-Warning $_
				return
			}
		}

		Write-Verbose "Validating imported package manifest '$($p.PackageName)' at '$($p.ManifestPath)'..."
		try {
			Confirm-Manifest $p.Manifest
		} catch {
			AddIssue "Validation of imported package manifest '$($p.PackageName)' at '$($p.ManifestPath)' failed: $_"
			return
		}
	}

	end {
		return $NoIssues
	}
}
