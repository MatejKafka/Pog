# Requires -Version 7
using module .\Paths.psm1
using module .\lib\Utils.psm1
using module .\lib\VersionParser.psm1
using module .\Common.psm1
using module .\Confirmations.psm1
using module .\container\Invoke-Container.psm1
. $PSScriptRoot\lib\header.ps1

# TODO: allow wildcards in PackageName and Version arguments where it makes sense
# TODO: allow pipelining for Import-, Install- and Enable-
#  (careful about overwriting parameters based on values of other parameters,
#   e.g. `Get-PogRepositoryPackage git, 7zip -LatestVersion | Import-Pog` will attempt to write 7zip manifest to `git` target)

# FIXME: this should be namespaced (and probably moved to a C# module and .Path and .Directory should be lazy getters)
class PogRepositoryPackage {
	[string]$PackageName
	[PogPackageVersion]$Version
	hidden [string]$Path
	hidden [string]$ManifestPath
	#hidden [System.IO.DirectoryInfo]$Directory

	PogRepositoryPackage([string]$PackageName, [PogPackageVersion]$Version) {
		$this.PackageName = $PackageName
		$this.Version = $Version
		$this.Path = Join-Path $script:MANIFEST_REPO $PackageName $Version.ToString()
		$this.ManifestPath = Get-ManifestPath $this.Path -NoError
		#$this.Directory = Get-Item $this.Path
	}

	PogRepositoryPackage([string]$PackageName, [PogPackageVersion]$Version, [string]$Path, [string]$ManifestPath) {
		$this.PackageName = $PackageName
		$this.Version = $Version
		$this.Path = $Path
		$this.ManifestPath = $ManifestPath
		#$this.Directory = Get-Item $this.Path
	}

	# TODO: change into a getter, update usages
	hidden [bool]Exists() {
		return Test-Path -Type Container $this.Path
	}
}

class PogImportedPackage {
	[string]$PackageName
	[PogPackageVersion]$Version
	[string]$Path
	#hidden [System.IO.DirectoryInfo]$Directory
	hidden [string]$ManifestName
	hidden [string]$ManifestPath

	PogImportedPackage([string]$PackageName, [string]$Path, [string]$ManifestPath, [string]$ManifestName, [PogPackageVersion]$ManifestVersion) {
		$this.PackageName = $PackageName
		$this.Path = $Path
		#$this.Directory = Get-Item $this.Path
		$this.ManifestPath = $ManifestPath
		$this.ManifestName = $ManifestName
		$this.Version = $ManifestVersion
	}
}


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

# whew, that's a lot of types...
class ManifestVersionCompleter : System.Management.Automation.IArgumentCompleter {
	[System.Collections.Generic.IEnumerable[System.Management.Automation.CompletionResult]]
	CompleteArgument(
			[string]$CommandName, [string]$ParameterName, [string]$WordToComplete,
			[System.Management.Automation.Language.CommandAst]$Ast,
			[System.Collections.IDictionary]$BoundParameters
	) {
		$ResultList = [System.Collections.Generic.List[System.Management.Automation.CompletionResult]]::new()
		try {
			if (@($BoundParameters.PackageName).Count -gt 1) {
				return $ResultList # cannot set version when multiple package names are specified
			}
			$RepoPackageDir = Get-Item (Join-Path $script:MANIFEST_REPO $BoundParameters.PackageName)
		} catch {
			return $ResultList
		}
		ls $RepoPackageDir | % Name | ? {$_.StartsWith($WordToComplete)} |
				% {New-PackageVersion $_} | sort -Descending | % {$ResultList.Add($_.ToString())}
		return $ResultList
	}
}


function Export-AppShortcuts {
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
		Join-Path $SYSTEM_START_MENU "Pog"
	} else {
		Join-Path $USER_START_MENU "Pog"
	}

	if (Test-Path $TargetDir) {
		Write-Information "Clearing previous Pog start menu entries..."
		Remove-Item -Recurse $TargetDir
	}

	Write-Information "Exporting shortcuts to '$TargetDir'."
	$null = New-Item -ItemType Directory $TargetDir

	$ShortcutCount = ls $PACKAGE_ROOTS -Directory `
		| % {Export-AppShortcuts $_.FullName $TargetDir} `
		| Measure-Object -Sum | % Sum
	Write-Information "Exported $ShortcutCount shortcuts."
}


Export function Get-RepositoryPackage {
	[CmdletBinding(DefaultParameterSetName="Version")]
	[OutputType([PogRepositoryPackage])]
	param(
			[Parameter(Position=0)]
			[ValidateSet([RepoPackageName])]
			[string[]]
		$PackageName,
			# TODO: figure out how to remove this parameter when -PackageName is an array
			[Parameter(Position=1, ParameterSetName="Version")]
			[ArgumentCompleter([ManifestVersionCompleter])]
			[ValidateScript({ValidateFileName})]
			[string]
		$Version,
			[Parameter(ParameterSetName="LatestVersion")]
			[switch]
		$LatestVersion
	)

	if ($Version) {
		if (-not $PackageName) {throw "-Version must not be passed without also passing -PackageName."}
		if (@($PackageName).Count) {throw "-Version must not be passed when -PackageName contains multiple package names."}
	}

	if ($PackageName -and $Version) {
		$p = [PogRepositoryPackage]::new($PackageName, $Version)
		if (-not $p.Exists()) {
			throw "Package '$PackageName' does not have version '$Version'."
		}
		return $p
	}

	if (-not $PackageName) {
		[string[]]$PackageName = [RepoPackageName]::new().GetValidValues()
	}
	foreach ($p in $PackageName) {
		# we have to parse the versions anyway, to find the latest ones
		# TODO: do max instead of sort if -LatestVersion is set
		$Versions = ls (Join-Path $script:MANIFEST_REPO $p) | % Name | % {New-PackageVersion $_} | sort
		if ($LatestVersion) {
			$Versions = $Versions | select -Last 1
		}
		foreach ($v in $Versions) {
			[PogRepositoryPackage]::new($p, $v)
		}
	}
}

Export function Get-Package {
	[CmdletBinding()]
	[OutputType([PogImportedPackage])]
	param(
			[ValidateSet([ImportedPackageName])]
			[string[]]
		$PackageName = [ImportedPackageName]::new().GetValidValues()
	)

	begin {
		foreach ($p in $PackageName) {
			# FIXME: duplicated between multiple functions
			$PackagePath = Get-PackageDirectory $p
			# get the name from the resolved directory, so that the casing is correct
			$p = Split-Path -Leaf $PackagePath
			$ManifestPath = Get-ManifestPath $PackagePath
			$Manifest = Import-PackageManifestFile $ManifestPath

			# FIXME: duplicated between multiple functions
			$ManifestName = if ($Manifest.ContainsKey("Name")) {$Manifest.Name} else {$null}
			$ManifestVersion = if ($Manifest.ContainsKey("Version")) {New-PackageVersion $Manifest.Version} else {$null}
			[PogImportedPackage]::new($p, $PackagePath, $ManifestPath, $ManifestName, $ManifestVersion)
		}
	}
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
		Write-Information "Passed path is already a package root: $Resolved."
		return
	}

	[void]$PACKAGE_ROOTS.Add($Resolved.Path)
	FlushPackageRootList
	Write-Information "Added $Resolved as package root."
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
		Write-Information "Removed unresolved package root $Resolved."
		return
	}

	$PACKAGE_ROOTS.Remove($Resolved)
	FlushPackageRootList
	Write-Information "Removed $Resolved from package root list."
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
	$CacheEntries = ls -Directory $DOWNLOAD_CACHE_DIR | % {
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


Export function Enable- {
	# .SYNOPSIS
	#	Enables an installed package to allow external usage.
	# .DESCRIPTION
	#	Enables an installed package, setting up required files and exporting public commands and shortcuts.
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName,
			### Extra parameters to pass to the Enable script in the package manifest. For interactive usage,
			### prefer to use the automatically generated parameters on this command (e.g. instead of passing
			### `@{Arg = Value}` to this parameter, pass `--Arg Value` as a standard parameter to this cmdlet),
			### which gives you autocomplete and name/type checking.
			[Hashtable]
		$PackageParameters = @{},
			# allows overriding existing commands without confirmation
			[switch]
		$Force,
			<# Return a PogImportedPackage object with information about the enabled package. #>
			[switch]
		$PassThru
	)

	dynamicparam {
		if (-not $PSBoundParameters.ContainsKey("PackageName")) {return}

		$CopiedParams = Copy-ManifestParameters $PackageName Enable -NamePrefix "-"
		# could not copy parameters (probably -PackageName not set yet)
		if ($null -eq $CopiedParams) {return}
		$function:ExtractParamsFn = $CopiedParams.ExtractFn
		return $CopiedParams.Parameters
	}

	begin {
		$PackagePath = Get-PackageDirectory $PackageName
		# get the name from the resolved directory, so that the casing is correct
		$PackageName = Split-Path -Leaf $PackagePath
		$ManifestPath = Get-ManifestPath $PackagePath
		$Manifest = Import-PackageManifestFile $ManifestPath

		$ForwardedParams = ExtractParamsFn $PSBoundParameters
		try {
			$PackageParameters += $ForwardedParams
		} catch {
			$CmdName = $MyInvocation.MyCommand.Name
			throw "The same parameter was passed to '${CmdName}' both using '-PackageParameters' and forwarded dynamic parameter. " +`
					"Each parameter must be present in at most one of these: " + $_
		}

		Confirm-Manifest $Manifest

		if ($Manifest.ContainsKey("Private") -and $Manifest.Private) {
			if ($Manifest.ContainsKey("Enable")) {
				Write-Information "Enabling private package '$PackageName'..."
			} else {
				Write-Information "Private package '$PackageName' does not have an enabler script."
				return
			}
		} elseif ($Manifest.Name -eq $PackageName) {
			Write-Information "Enabling package '$($Manifest.Name)', version '$($Manifest.Version)'..."
		} else {
			Write-Information "Enabling package '$($Manifest.Name)' (installed as '$PackageName'), version '$($Manifest.Version)'..."
		}

		$InternalArgs = @{
			AllowOverwrite = [bool]$Force
		}

		Invoke-Container Enable $PackageName $ManifestPath $PackagePath $InternalArgs $PackageParameters
		Write-Information "Successfully enabled $PackageName."
		if ($PassThru) {
			$ManifestName = if ($Manifest.ContainsKey("Name")) {$Manifest.Name} else {$null}
			$ManifestVersion = if ($Manifest.ContainsKey("Version")) {New-PackageVersion $Manifest.Version} else {$null}
			return [PogImportedPackage]::new($PackageName, $PackagePath, $ManifestPath, $ManifestName, $ManifestVersion)
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
	[OutputType([PogImportedPackage])]
	param(
			### Name of the package to install. This is the install name, not necessarily the manifest app name.
			[Parameter(Mandatory)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName,
			# TODO: remove this when removing the option to use scriptblock for the Install block

			### Extra parameters to pass to the Install script in the package manifest. For interactive usage,
			### prefer to use the automatically generated parameters on this command (e.g. instead of passing
			### `@{Arg = Value}` to this parameter, pass `--Arg Value` as a standard parameter to this cmdlet),
			### which gives you autocomplete and name/type checking.
			[Hashtable]
		$PackageParameters = @{},
			### If set, and some version of the package is already installed, prompt before overwriting
			### with the current version according to the manifest.
			[switch]
		$Confirm,
			### If set, files are downloaded with low priority, which results in better network responsiveness
			### for other programs, but possibly slower download speed.
			[switch]
		$LowPriority,
			<# Return a PogImportedPackage object with information about the installed package. #>
			[switch]
		$PassThru
	)

	dynamicparam {
		if (-not $PSBoundParameters.ContainsKey("PackageName")) {return}

		$CopiedParams = Copy-ManifestParameters $PackageName Install -NamePrefix "-"
		if ($null -eq $CopiedParams) {return}
		$function:ExtractParamsFn = $CopiedParams.ExtractFn
		return $CopiedParams.Parameters
	}

	begin {
		# FIXME: currently, we load the manifest 3 times before it's actually executed (once for dynamicparam, second here, third inside the container)
		$PackagePath = Get-PackageDirectory $PackageName
		# get the name from the resolved directory, so that the casing is correct
		$PackageName = Split-Path -Leaf $PackagePath
		$ManifestPath = Get-ManifestPath $PackagePath
		$Manifest = Import-PackageManifestFile $ManifestPath -NoUnwrap

		$ForwardedParams = ExtractParamsFn $PSBoundParameters
		try {
			$PackageParameters += $ForwardedParams
		} catch {
			$CmdName = $MyInvocation.MyCommand.Name
			throw "The same parameter was passed to '${CmdName}' both using '-PackageParameters' and forwarded dynamic parameter. " +`
					"Each parameter must be present in at most one of these: " + $_
		}

		Confirm-Manifest $Manifest

		# Name is not required for private packages
		if (-not $Manifest.ContainsKey("Name")) {
			if ($Manifest.ContainsKey("Install")) {
				Write-Information "Installing private package '$PackageName'..."
			} else {
				Write-Information "Private package '$PackageName' does not have an installation script."
				return
			}
		} elseif ($Manifest.Name -eq $PackageName) {
			Write-Information "Installing package '$($Manifest.Name)', version '$($Manifest.Version)'..."
		} else {
			Write-Information "Installing package '$($Manifest.Name)' (installed as '$PackageName'), version '$($Manifest.Version)'..."
		}

		$InternalArgs = @{
			AllowOverwrite = -not [bool]$Confirm
			DownloadLowPriority = [bool]$LowPriority
		}

		Invoke-Container Install $PackageName $ManifestPath $PackagePath $InternalArgs $PackageParameters
		Write-Information "Successfully installed $PackageName."
		if ($PassThru) {
			$ManifestName = if ($Manifest.ContainsKey("Name")) {$Manifest.Name} else {$null}
			$ManifestVersion = if ($Manifest.ContainsKey("Version")) {New-PackageVersion $Manifest.Version} else {$null}
			return [PogImportedPackage]::new($PackageName, $PackagePath, $ManifestPath, $ManifestName, $ManifestVersion)
		}
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
			[Parameter(Mandatory)]
			[string]
		$ImportedVersion,
			[Hashtable]
		$Manifest
	)

	$Title = "Overwrite existing package manifest?"
	$ManifestDescription = if ($null -eq $Manifest) {""}
			else {" (manifest '$($Manifest.Name)', version '$($Manifest.Version)')"}
	$Message = "There is already an imported package with name '$TargetName' " +`
			"in '$TargetPackageRoot'$ManifestDescription. Overwrite its manifest with version '$ImportedVersion'?"
	return Confirm-Action $Title $Message -ActionType "ManifestOverwrite"
}

function ValidateFileName {
	if (-not $_) {return $true}
	$InvalidI = $_.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars())
	if ($InvalidI -ge 0) {throw "Must be a valid directory name, cannot contain invalid characters like '$($_[$InvalidI])': $_"}
	if ($_ -eq "." -or $_ -eq "..") {throw "Must be a valid directory name, not '.' or '..': $_"}
	return $true
}

Export function Import- {
	# .SYNOPSIS
	#	Imports a package manifest from the repository.
	[CmdletBinding(PositionalBinding = $false)]
	[OutputType([PogImportedPackage])]
	Param(
			[Parameter(Mandatory, Position = 0)]
			[ValidateSet([RepoPackageName])]
			[string]
		$PackageName,
			[Parameter(Position = 1)]
			[ArgumentCompleter([ManifestVersionCompleter])]
			[ValidateScript({ValidateFileName})]
			[string]
		$Version,
			[string]
		$TargetName,
			[ValidateSet([PackageRoot])]
			[string]
		$TargetPackageRoot = $script:PACKAGE_ROOTS[0],
			<# Overwrite an existing package without prompting for confirmation. #>
			[switch]
		$Force,
			<# Return a PogImportedPackage object with information about the imported package. #>
			[switch]
		$PassThru
	)

	$RepoPackageDir = Get-Item (Join-Path $script:MANIFEST_REPO $PackageName)
	# get the name from the repository, so that the casing is correct
	$PackageName = $RepoPackageDir.Name

	if (-not $TargetName) {
		# this must be done after the $PackageName update above
		$TargetName = $PackageName
	}

	if (-not $Version) {
		# find latest version
		$Version = Get-LatestPackageVersion $RepoPackageDir
	} elseif (-not (Test-Path (Join-Path $RepoPackageDir $Version))) {
		throw "Unknown version of package '$PackageName': $Version"
	}

	Write-Verbose "Validating the manifest before importing..."
	if (-not (Confirm-RepositoryPackage $PackageName $Version)) {
		throw "Validation of the repository manifest failed (see warnings above), refusing to import."
	}

	$SrcPath = Join-Path $RepoPackageDir $Version
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

		if (-not $Force -and -not (ConfirmManifestOverwrite $TargetName $TargetPackageRoot $Version $OrigManifest)) {
			throw "There is already a package with name '$TargetName' in '$TargetPackageRoot'. Pass -Force to overwrite the current manifest without confirmation."
		}
		Write-Information "Overwriting previous package manifest..."
		$script:MANIFEST_CLEANUP_PATHS | % {Join-Path $TargetPath $_} | ? {Test-Path $_} | Remove-Item -Recurse
	} else {
		$null = New-Item -Type Directory $TargetPath
	}

	ls $SrcPath | Copy-Item -Destination $TargetPath -Recurse
	Write-Information "Initialized '$TargetPath' with package manifest '$PackageName' (version '$Version')."
	if ($PassThru) {
		return [PogImportedPackage]::new($TargetName, $TargetPath, (Get-ManifestPath $TargetPath), $PackageName, (New-PackageVersion $Version))
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
			[ValidateScript({ValidateFileName})]
			[string]
		$Version,
			### If set, files are downloaded with low priority, which results in better network responsiveness
			### for other programs, but possibly slower download speed.
			[switch]
		$LowPriority
	)
	# TODO: -PassThru to return structured object instead of Write-Host pretty-print

	process {
		$RepoPackageDir = Get-Item (Join-Path $script:MANIFEST_REPO $PackageName)
		# get the name from the repository, so that the casing is correct
		$PackageName = $RepoPackageDir.Name

		if (-not $Version) {
			# find latest version
			$Version = Get-LatestPackageVersion (Join-Path $script:MANIFEST_REPO $PackageName)
		} elseif (-not (Test-Path (Join-Path $RepoPackageDir $Version))) {
			throw "Unknown version of package '$PackageName': $Version"
		}
		$ManifestDir = Join-Path $RepoPackageDir $Version
		$ManifestPath = Get-ManifestPath $ManifestDir

		if (-not (Confirm-RepositoryPackage $PackageName $Version)) {
			throw "Validation of the repository manifest failed (see warnings above)."
		}

		$InternalArgs = @{
			AllowOverwrite = $true # not used
			DownloadLowPriority = [bool]$LowPriority
		}

		Invoke-Container GetInstallHash $PackageName $ManifestPath $ManifestDir $InternalArgs @{}
	}
}

function FillManifestTemplate($PackageName, $Version, $ManifestTemplatePath) {
	$Manifest = Get-Content -Raw $ManifestTemplatePath
	$Manifest = $Manifest.Replace("'{{NAME}}'", "'" + $PackageName.Replace("'", "''") + "'")
	$Manifest = $Manifest.Replace("'{{VERSION}}'", "'" + $Version.Replace("'", "''") + "'")
	return $Manifest
}

Export function New-Manifest {
	[CmdletBinding()]
	[OutputType([PogRepositoryPackage])]
	param(
			[Parameter(Mandatory)]
			[ValidateScript({ValidateFileName})]
			[string]
		$PackageName,
			[Parameter(Mandatory)]
			[ValidateScript({ValidateFileName})]
			[string]
		$Version
	)

	# TODO: validate state of the package directory (check if it's not empty after error,...)
	begin {
		# parse to check validity
		$ParsedVersion = New-PackageVersion $Version
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

		$ManifestPath = Join-Path $ManifestDir $script:MANIFEST_REL_PATH
		$TemplatePath = "$PSScriptRoot\resources\repository_manifest_template.psd1"
		$null = New-Item -Path $ManifestPath -Value (FillManifestTemplate $PackageName $Version $TemplatePath)

		return [PogRepositoryPackage]::new($PackageName, $ParsedVersion, $ManifestDir, $ManifestPath)
	}
}

Export function New-DirectManifest {
	[CmdletBinding()]
	[OutputType([PogImportedPackage])]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName
	)

	begin {
		$PackagePath = Get-PackageDirectory $PackageName

		$ManifestPath = try {
			Get-ManifestPath $PackagePath
		} catch {
			Resolve-VirtualPath (Join-Path $PackagePath $script:MANIFEST_REL_PATH)
		}

		if (Test-Path $ManifestPath) {
			throw "Package $PackageName already has a manifest at '$ManifestPath'."
		}

		$null = Copy-Item $PSScriptRoot\resources\direct_manifest_template.psd1 $ManifestPath -PassThru
		return [PogImportedPackage]::new($PackageName, $PackagePath, $ManifestPath, $null, $null)
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
				Version = [string]$VersionStr
				# store the original value, so that we can pass it unchanged to the manifest generator
				OriginalValue = $_
			}
		}
}

# TODO: run this inside a container (make sure VersionParser module is available inside it)
# TODO: set working directory of the generator script to the target dir?
<# Generates manifests for new versions of the package. First checks for new versions,
   then calls the manifest generator for each version. #>
Export function Update-Manifest {
	[CmdletBinding()]
	[OutputType([PogRepositoryPackage])]
	param(
			[Parameter(ValueFromPipeline)]
			[ValidateSet([RepoManifestGeneratorName])]
			[string]
		$PackageName,
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
			Write-Information "Updating manifest repository for all packages..."
			# call itself with all existing manifest generators
			[RepoManifestGeneratorName]::new().GetValidValues() | & $MyInvocation.MyCommand @PSBoundParameters
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

		$GenDir = Join-Path $script:MANIFEST_GENERATOR_REPO $PackageName
		$ManifestDir = Join-Path $script:MANIFEST_REPO $PackageName

		if (-not (Test-Path $ManifestDir)) {
			$null = mkdir $ManifestDir
		}

		try {
			# list available versions without existing manifest (unless -Force is set, then all versions are listed)
			# only generate manifests for versions that don't already exist, unless -Force is passed
			$ExistingVersions = ls -Directory $ManifestDir | % {$_.Name}

			$GeneratedVersions = RetrievePackageVersions $PackageName $GenDir `
				# if -Force was not passed, filter out versions with already existing manifest
				| ? {$Force -or $_.Version -notin $ExistingVersions} `
				# if $Version was passed, filter out the versions; as the versions generated
				#  by the script may have other metadata, we cannot use the versions passed in $Version directly
				| ? {-not $Version -or $_.Version -in $Version}

			if ($Version -and @($Version).Count -ne @($GeneratedVersions).Count) {
				$FoundVersions = $GeneratedVersions | % {$_.Version}
				$MissingVersionsStr = $Version | ? {$_ -notin $FoundVersions} | Join-String -Separator ", "
				throw "Some of the package versions passed in -Version were not found for package '$PackageName': $MissingVersionsStr" +`
						"  (Are you sure these versions exist?)"
			}

			if ($ListOnly) {
				# useful for testing if all expected versions are retrieved
				foreach ($v in $GeneratedVersions) {
					[PogRepositoryPackage]::new($PackageName, (New-PackageVersion $v.Version))
				}
				return
			}

			# generate manifest for each version
			$GeneratedVersions | % {
				$TargetDir = Join-Path $ManifestDir $_.Version
				if (-not (Test-Path $TargetDir)) {$null = mkdir $TargetDir}
				try {
					$null = & "$GenDir\generator.ps1" $_.OriginalValue $TargetDir
					if (-not (ls $TargetDir | select -First 1)) {
						# target dir is empty, nothing was generated
						rm $TargetDir
						# TODO: also check if the manifest file itself is generated, or possibly run full validation (Confirm-RepositoryPackage)
						throw "Manifest generator for package '$PackageName', version '$($_.Version) did not generate any files."
					} else {
						[PogRepositoryPackage]::new($PackageName, (New-PackageVersion $_.Version))
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
			[ArgumentCompleter([ManifestVersionCompleter])]
			[ValidateScript({ValidateFileName})]
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

			foreach ($f in ls $_) {
				if ($f.Name -notin "pog.psd1", ".pog") {
					AddIssue ("Manifest directory for '$PackageName', version '$Version' contains extra file/directory '$($f.Name)' at '$f'." `
							+ " Each manifest directory must only contain a pog.psd1 manifest file and an optional .pog directory for extra files.")
				}
			}

			$ExtraFileDir = Get-Item "$_\.pog" -ErrorAction Ignore
			if ($ExtraFileDir -and $ExtraFileDir -isnot [System.IO.DirectoryInfo]) {
				AddIssue ("'$ExtraFileDir' should be a directory, not a file, in manifest directory for '$PackageName', version '$Version'.")
			}

			try {
				$ManifestPath = Get-ManifestPath $_
			} catch {
				AddIssue ("Could not find manifest '$PackageName', version '$Version': " +`
 						$_.ToString().Replace("`n", "`n    "))
				return
			}

			try {
				$Manifest = Import-PackageManifestFile $ManifestPath
			} catch {
				AddIssue $_
				return
			}

			try {
				Confirm-Manifest $Manifest $PackageName $Version -IsRepositoryManifest
			} catch {
				AddIssue ("Validation of package manifest '$PackageName', version '$Version' from local repository failed." +`
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
Export function Confirm-Package {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory, ValueFromPipeline)]
			[ValidateSet([ImportedPackageName])]
			[string]
		$PackageName
	)

	process {
		$PackagePath = Get-PackageDirectory $PackageName
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
