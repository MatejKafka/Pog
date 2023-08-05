# Requires -Version 7
using module .\Paths.psm1
using module .\lib\Utils.psm1
using module .\Common.psm1
using module .\Confirmations.psm1
using module .\lib\Copy-CommandParameters.psm1
. $PSScriptRoot\lib\header.ps1


Export function Get-PogRepositoryPackage {
	# .SYNOPSIS
	#	Lists packages available in the package repository.
	# .DESCRIPTION
	#	The `Get-PogRepositoryPackage` cmdlet lists packages from the package repository.
	#	Each package is represented by a single `Pog.RepositoryPackage` instance. By default, only the latest version
	#	of each package is returned. If you want to list all available versions, use the `-AllVersions` switch parameter.
	[CmdletBinding(DefaultParameterSetName="Version")]
	[OutputType([Pog.RepositoryPackage])]
	param(
			### Names of packages to return. If not passed, all repository packages are returned.
			[Parameter(Position=0, ValueFromPipeline, ValueFromPipelineByPropertyName)]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageNameCompleter])]
			[string[]]
		$PackageName,
			# TODO: figure out how to remove this parameter when -PackageName is an array

			### Return only a single package with the given version. An exception is thrown if the version is not found.
			[Parameter(Position=1, ParameterSetName="Version")]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageVersionCompleter])]
			[Pog.PackageVersion]
		$Version,
			### Return all available versions of each repository package. By default, only the latest one is returned.
			[Parameter(ParameterSetName="AllVersions")]
			[switch]
		$AllVersions
	)

	begin {
		if ($Version) {
			if ($MyInvocation.ExpectingInput) {throw "-Version must not be passed together with pipeline input."}
			if (-not $PackageName) {throw "-Version must not be passed without also passing -PackageName."}
			if (@($PackageName).Count -gt 1) {throw "-Version must not be passed when -PackageName contains multiple package names."}

			try {
				return $REPOSITORY.GetPackage($PackageName, $true, $true).GetVersionPackage($Version, $true)
			} catch [Pog.RepositoryPackageNotFoundException], [Pog.RepositoryPackageVersionNotFoundException] {
				$PSCmdlet.ThrowTerminatingError($_)
			}
		}

		# by default, return all available packages
		if (-not $PSBoundParameters.ContainsKey("PackageName") -and -not $MyInvocation.ExpectingInput) {
			$PackageName = $REPOSITORY.EnumeratePackageNames()
		}
	}

	process {
		if ($Version) {return} # already processed
		foreach ($pn in $PackageName) {& {
			$ErrorActionPreference = "Continue"
			try {
				# TODO: use Enumerate($pn) to support wildcards and handle arrays in the rest of the code
				#  same for Get-PogPackage
				$c = $REPOSITORY.GetPackage($pn, $true, $true)
			} catch [Pog.RepositoryPackageNotFoundException], [Pog.InvalidPackageNameException] {
				$PSCmdlet.WriteError($_)
				continue
			}
			$ErrorActionPreference = "Stop"
			if ($AllVersions) {
				echo $c.Enumerate()
			} else {
				echo $c.GetLatestPackage()
			}
		}}
	}
}

Export function Get-PogPackage {
	# .SYNOPSIS
	#	Lists installed packages.
	# .DESCRIPTION
	#	The `Get-PogPackage` cmdlet lists installed packages. Each package is represented by a single `Pog.ImportedPackage` instance.
	#	By default, packages from all package roots are returned, unless the `-PackageRoot` parameter is set.
	[CmdletBinding()]
	[OutputType([Pog.ImportedPackage])]
	param(
			### Names of installed packages to return. If not passed, all installed packages are returned.
			[Parameter(ValueFromPipeline, ValueFromPipelineByPropertyName)]
			[ArgumentCompleter([Pog.PSAttributes.ImportedPackageNameCompleter])]
			[string[]]
		$PackageName,
			### Path to the content root in which to list packages. If not passed, installed packages from all package roots are returned.
			[ArgumentCompleter([Pog.PSAttributes.ValidPackageRootPathCompleter])]
			[string]
		$PackageRoot
	)

	process {
		if ($PackageRoot) {
			$PackageRoot = try {
				$PACKAGE_ROOTS.ResolveValidPackageRoot($PackageRoot)
			} catch [Pog.PackageRootNotValidException] {
				$PSCmdlet.ThrowTerminatingError($_)
			}
		}

		if (-not $PackageName) {
			# do not eagerly load the manifest
			if ($PackageRoot) {
				echo $PACKAGE_ROOTS.EnumeratePackages($PackageRoot, $false)
			} else {
				echo $PACKAGE_ROOTS.EnumeratePackages($false)
			}
		} else {
			$ErrorActionPreference = "Continue"
			foreach ($p in $PackageName) {
				try {
					if ($PackageRoot) {
						# do not eagerly load the manifest ---------------------\/
						echo $PACKAGE_ROOTS.GetPackage($p, $PackageRoot, $true, $false)
					} else {
						# do not eagerly load the manifest -------\/
						echo $PACKAGE_ROOTS.GetPackage($p, $true, $false)
					}
				} catch [Pog.ImportedPackageNotFoundException], [Pog.InvalidPackageNameException] {
					$PSCmdlet.WriteError($_)
				}
			}
		}
	}
}

Export function Get-PogRoot {
	# .SYNOPSIS
	#	Returns a list of all registered package roots, even obsolete (non-existent) ones.
	[CmdletBinding()]
	[OutputType([string])]
	param()
	return $PACKAGE_ROOTS.PackageRoots.AllPackageRoots
}

# functions to programmatically add/remove package roots are intentionally not provided, because it is a bit non-trivial
#  to get the file updates right from a concurrency perspective
# TODO: ^ figure out how to provide the functions safely
Export function Edit-PogRootList {
	$Path = $PACKAGE_ROOTS.PackageRoots.PackageRootFile
	Write-Information "Opening the package root list at '$Path' for editing in a text editor..."
	Write-Information "Each line should contain a single absolute path to the package root directory."
	# this opens the file for editing in a text editor (it's a .txt file)
	Start-Process $Path
}

Export function Clear-PogDownloadCache {
	# .SYNOPSIS
	#	Removes all cached package archives in the local download cache that are older than the specified date.
	# .DESCRIPTION
	#   The `Clear-PogDownloadCache` cmdlet lists all package archives stored in the local download cache, which are older than the specified date.
	#   After confirmation, the archives are deleted. If an archive is currently used (the package is currently being installed), a warning
	#   is printed, but the matching remaining entries are deleted.
	[CmdletBinding(DefaultParameterSetName = "Days")]
	param(
			[Parameter(Mandatory, ParameterSetName = "Date", Position = 0)]
			[DateTime]
		$DateBefore,
			[Parameter(ParameterSetName = "Days", Position = 0)]
			[int]
		$DaysBefore = 0,
			### Do not prompt for confirmation and delete the cache entries immediately.
			[switch]
		$Force
	)

	if ($PSCmdlet.ParameterSetName -eq "Days") {
		$DateBefore = [DateTime]::Now.AddDays(-$DaysBefore)
	}

	$Entries = $DOWNLOAD_CACHE.EnumerateEntries({param($err)
		Write-Warning "Invalid cache entry encountered, deleting...: $($err.EntryKey)"
		try {$DOWNLOAD_CACHE.DeleteEntry($err.EntryKey)} catch [Pog.CacheEntryInUseException] {
			Write-Warning "Cannot delete the invalid entry, it is currently in use."
		}
	}) | ? {$_.LastUseTime -le $DateBefore <# keep recently used entries #>}

	if (@($Entries).Count -eq 0) {
		if ($Force) {
			Write-Information "No package archives older than '$($DateBefore.ToString())' found, nothing to remove."
			return
		} else {
			throw "No cached package archives downloaded before '$($DateBefore.ToString())' found."
		}
	}

	if ($Force) {
		# do not check for confirmation
		$SizeSum = 0
		$DeletedCount = 0
		$Entries | % {
			$ErrorActionPreference = "Continue"
			$SizeSum += $_.Size
			$DeletedCount += 1
			try {$DOWNLOAD_CACHE.DeleteEntry($_)}
			catch [Pog.CacheEntryInUseException] {$PSCmdlet.WriteError($_)}
		}
		Write-Information ("Removed $DeletedCount package archive" + $(if ($DeletedCount -eq 1) {""} else {"s"}) +`
						   ", freeing ~{0:F} GB of space." -f ($SizeSum / 1GB))
		return
	}

	# print the cache entry list
	$SizeSum = 0
	$Entries | sort Size -Descending | % {
		$SizeSum += $_.Size
		$SourceStr = $_.SourcePackages `
			| % {$_.PackageName + $(if ($_.ManifestVersion) {" v$($_.ManifestVersion)"})} `
			| Join-String -Separator ", "
		Write-Host ("{0,10:F2} MB - {1}" -f @(($_.Size / 1MB), $SourceStr))
	}

	$Title = "Remove the listed package archives, freeing ~{0:F} GB of space?" -f ($SizeSum / 1GB)
	$Message = "This will not affect already installed applications. Reinstallation of an application may take longer," + `
		" as it will have to be downloaded again."
	if (Confirm-Action $Title $Message) {
		# delete the entries
		$Entries | % {
			$ErrorActionPreference = "Continue"
			try {$DOWNLOAD_CACHE.DeleteEntry($_)}
			catch [Pog.CacheEntryInUseException] {$PSCmdlet.WriteError($_)}
		}
	} else {
		Write-Host "No package archives were removed."
	}
}


Export function Enable-Pog {
	# .SYNOPSIS
	#	Enables an installed package to allow external usage.
	# .DESCRIPTION
	#	Enables an installed package, setting up required files and exporting public commands and shortcuts.
	[CmdletBinding(PositionalBinding = $false, DefaultParameterSetName = "PackageName")]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(Mandatory, Position = 0, ParameterSetName = "Package", ValueFromPipeline)]
			[Pog.ImportedPackage[]]
		$Package,
			### Name of the package to enable. This is the target name, not necessarily the manifest app name.
			[Parameter(Mandatory, Position = 0, ParameterSetName = "PackageName", ValueFromPipeline)]
			[ArgumentCompleter([Pog.PSAttributes.ImportedPackageNameCompleter])]
			[string[]]
		$PackageName,
			### Extra parameters to pass to the Enable script in the package manifest. For interactive usage,
			### prefer to use the automatically generated parameters on this command (e.g. instead of passing
			### `@{Arg = Value}` to this parameter, pass `-_Arg Value` as a standard parameter to this cmdlet),
			### which gives you autocomplete and name/type checking.
			[Parameter(Position = 1)]
			[Hashtable]
		$PackageParameters = @{},
			### Allow overriding existing commands without confirmation.
			[switch]
		$Force,
			### Return a [Pog.ImportedPackage] object with information about the enabled package.
			[switch]
		$PassThru
	)

	dynamicparam {
		# remove possible leftover from previous dynamicparam invocation
		Remove-Variable CopiedParams -Scope Local -ErrorAction Ignore

		if (-not $PSBoundParameters.ContainsKey("PackageName") -and -not $PSBoundParameters.ContainsKey("Package")) {return}
		# TODO: make this work for multiple packages (probably by prefixing the parameter name with package name?)
		# more than one package, ignore package parameters
		if (@($PSBoundParameters["Package"]).Count -gt 1 -or @($PSBoundParameters["PackageName"]).Count -gt 1) {return}

		$p = if ($PSBoundParameters["Package"]) {$PSBoundParameters["Package"]} else {
			# $PackageName contains what's written at the command line, without any parsing or evaluation, we need to (try to) parse it
			$ParsedPackageName = [Pog.PSAttributes.ParameterQuotingHelper]::ParseDynamicparamArgumentLiteral($PSBoundParameters["PackageName"])
			# could not parse
			if (-not $ParsedPackageName) {return}
			# this may fail in case the package does not exist, or manifest is invalid
			# don't throw here, just return, the issue will be handled in the begin{} block
			try {$PACKAGE_ROOTS.GetPackage($ParsedPackageName, $true, $true)} catch {return}
		}
		$CopiedParams = Copy-ManifestParameters $p.Manifest Enable -NamePrefix "_"
		return $CopiedParams
	}

	begin {
		if ($PSBoundParameters.ContainsKey("PackageParameters")) {
			if ($MyInvocation.ExpectingInput) {throw "-PackageParameters must not be passed when packages are passed through pipeline."}
			if (@($PackageName).Count -gt 1) {throw "-PackageParameters must not be passed when -PackageName contains multiple package names."}
			if (@($Package).Count -gt 1) {throw "-PackageParameters must not be passed when -Package contains multiple packages."}
		}

		if (Get-Variable CopiedParams -Scope Local -ErrorAction Ignore) {
			# $p is already loaded
			$Package = $p
			$ForwardedParams = $CopiedParams.Extract($PSBoundParameters)
			try {
				$PackageParameters += $ForwardedParams
			} catch {
				$CmdName = $MyInvocation.MyCommand.Name
				throw "The same parameter was passed to '${CmdName}' both using '-PackageParameters' and forwarded dynamic parameter. " +`
						"Each parameter must be present in at most one of these: " + $_
			}
		} else {
			# either the package manifest is invalid, or multiple packages were passed, or pipeline input is used
		}
	}

	# TODO: do this in parallel (even for packages passed as array)
	process {
		$Packages = if ($Package) {$Package} else {& {
			$ErrorActionPreference = "Continue"
			foreach($pn in $PackageName) {
				try {
					$PACKAGE_ROOTS.GetPackage($pn, $true, $true)
				} catch [Pog.ImportedPackageNotFoundException] {
					$PSCmdlet.WriteError($_)
				}
			}
		}}

		foreach ($p in $Packages) {
			Confirm-Manifest $p.Manifest

			if (-not $p.Manifest.Raw.ContainsKey("Enable")) {
				Write-Information "Package '$($p.PackageName)' does not have an Enable block."
				continue
			}

			$InternalArgs = @{
				AllowOverwrite = [bool]$Force
			}

			Write-Information "Enabling $($p.GetDescriptionString())..."
			Invoke-Container Enable $p -InternalArguments $InternalArgs -PackageArguments $PackageParameters
			Write-Information "Successfully enabled '$($p.PackageName)'."
			if ($PassThru) {
				echo $p
			}
		}
	}
}

Export function Install-Pog {
	# .SYNOPSIS
	#	Downloads and extracts package files.
	# .DESCRIPTION
	#	Downloads and extracts package files, populating the ./app directory of the package. Downloaded files
	#	are cached, so repeated installs only require internet connection for the initial download.
	[CmdletBinding(PositionalBinding = $false, DefaultParameterSetName = "PackageName")]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(Mandatory, Position = 0, ParameterSetName = "Package", ValueFromPipeline)]
			[Pog.ImportedPackage[]]
		$Package,
			### Name of the package to install. This is the target name, not necessarily the manifest app name.
			[Parameter(Mandatory, Position = 0, ParameterSetName = "PackageName", ValueFromPipeline)]
			[ArgumentCompleter([Pog.PSAttributes.ImportedPackageNameCompleter])]
			[string[]]
		$PackageName,
			### If some version of the package is already installed, prompt before overwriting
			### with the current version according to the manifest.
			[switch]
		$Confirm,
			### Download files with low priority, which results in better network responsiveness
			### for other programs, but possibly slower download speed.
			[switch]
		$LowPriority,
			### Return a [Pog.ImportedPackage] object with information about the installed package.
			[switch]
		$PassThru
	)

	# TODO: do this in parallel (even for packages passed as array)
	process {
		$Packages = if ($Package) {$Package} else {& {
			$ErrorActionPreference = "Continue"
			foreach($pn in $PackageName) {
				try {
					$PACKAGE_ROOTS.GetPackage($pn, $true, $true)
				} catch [Pog.ImportedPackageNotFoundException] {
					$PSCmdlet.WriteError($_)
				}
			}
		}}

		foreach ($p in $Packages) {
			Confirm-Manifest $p.Manifest

			if (-not $p.Manifest.Raw.ContainsKey("Install")) {
				Write-Information "Package '$($p.PackageName)' does not have an Install block."
				continue
			}

			$InternalArgs = @{
				AllowOverwrite = -not [bool]$Confirm
				DownloadLowPriority = [bool]$LowPriority
			}

			Write-Information "Installing $($p.GetDescriptionString())..."
			# FIXME: probably discard container output, it breaks -PassThru
			Invoke-Container Install $p -InternalArguments $InternalArgs
			Write-Information "Successfully installed '$($p.PackageName)'."
			if ($PassThru) {
				echo $p
			}
		}
	}
}

function ConfirmManifestOverwrite([Pog.ImportedPackage]$p, $TargetPackageRoot, [Pog.RepositoryPackage]$SrcPackage, [switch]$Force) {
	$OrigManifest = $null
	try {
		# try to load the (possibly) existing manifest
		$p.ReloadManifest()
		$OrigManifest = $p.Manifest
	} catch [System.IO.DirectoryNotFoundException] {
		# the package does not exist, no need to confirm
		return $true
	} catch [Pog.PackageManifestNotFoundException] {
		# the package exists, but the manifest is missing
		# either a random folder was erroneously created, or this is a package, but corrupted
		Write-Warning ("A package directory already exists at '$($p.Path)'," `
				+ " but it doesn't seem to contain a package manifest." `
				+ " All directories in a package root should be packages with a valid manifest.")
	} catch [Pog.PackageManifestParseException] {
		# the package has a manifest, but it's invalid (probably corrupted)
		Write-Warning ("Found an existing package manifest in '$($p.Path)', but it's not valid." `
				+ " Call 'Confirm-PogPackage `"$($p.PackageName)`"' to get more detailed information.")
	}

	if (-not $Force) {
		# prompt for confirmation
		$Title = "Overwrite an existing package manifest for '$($p.PackageName)'?"
		$ManifestDescription = if ($null -eq $OrigManifest) {""}
				else {" (manifest '$($OrigManifest.Name)', version '$($OrigManifest.Version)')"}
		$Message = "There is already an imported package '$($p.PackageName)'" `
				+ " in '$TargetPackageRoot'$ManifestDescription. Overwrite its manifest with version '$($SrcPackage.Version)'?"
		if (-not (Confirm-Action $Title $Message -ActionType "ManifestOverwrite")) {
			return $false
		}
	}

	return $true
}

# TODO: allow wildcards in PackageName and Version arguments for commands where it makes sense
Export function Import-Pog {
	# .SYNOPSIS
	#	Imports a package manifest from the repository.
	# .DESCRIPTION
	#	Imports a package from the repository by copying the package manifest to the target path,
	#	where it can be installed by calling `Install-Pog` and the remaining installation stage cmdlets.
	[CmdletBinding(PositionalBinding = $false, DefaultParameterSetName = "Separate")]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(Mandatory, Position = 0, ParameterSetName = "RepositoryPackage", ValueFromPipeline)]
			[Pog.RepositoryPackage[]]
		$Package,
			### Names of the repository packages to import.
			[Parameter(Mandatory, ParameterSetName = "PackageName", ValueFromPipeline)]
			[Parameter(Mandatory, Position = 0, ParameterSetName = "Separate")]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageNameCompleter])]
			[string[]]
		$PackageName,
			### Specific version of the package to import. By default, the latest version is imported.
			[Parameter(Position = 1, ParameterSetName = "Separate")]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageVersionCompleter])]
			[Pog.PackageVersion]
		$Version,
			### Name of the imported package. By default, this is the same as the repository package name.
			### Use this parameter to distinguish multiple installations of the same package.
			[Parameter(ParameterSetName = "Separate")]
			[ArgumentCompleter([Pog.PSAttributes.ImportedPackageNameCompleter])]
			[string]
		$TargetName,
			### Path to a registered package root, where the package should be imported.
			### If not set, the default (first) package root is used.
			[ArgumentCompleter([Pog.PSAttributes.ValidPackageRootPathCompleter])]
			[string]
		$TargetPackageRoot = $PACKAGE_ROOTS.DefaultPackageRoot,
			### Overwrite an existing package without prompting for confirmation.
			[switch]
		$Force,
			### Return a [Pog.ImportedPackage] object with information about the imported package.
			[switch]
		$PassThru
	)

	# NOTES:
	#  - always use $SrcPackage.PackageName instead of $PackageName, which may not have the correct casing
	#  - same for $ResolvedTargetName and $TargetName
	#
	# supported usages:
	#  - Import-Pog git -Version ...
	#  - Import-Pog neovim, git  # no Version and TargetName
	#  - "neovim", "git" | Import-Pog  # no Version and TargetName
	#  - Get-PogRepositoryPackage -LatestVersion neovim, git | Import-Pog  # no Version and TargetName

	begin {
		if ($Version) {
			if ($MyInvocation.ExpectingInput) {throw "-Version must not be passed when -PackageName is passed through pipeline."}
			if (@($PackageName).Count -gt 1) {throw "-Version must not be passed when -PackageName contains multiple package names."}
		}

		$TargetPackageRoot = if (-not $TargetPackageRoot) {
			$PACKAGE_ROOTS.DefaultPackageRoot
		} else {
			try {$PACKAGE_ROOTS.ResolveValidPackageRoot($TargetPackageRoot)}
			catch [Pog.PackageRootNotValidException] {
				$PSCmdlet.ThrowTerminatingError($_)
			}
		}
	}

	process {
		if ($Package) {
			$SrcPackages = $Package
		} else {
			$SrcPackages = foreach ($n in $PackageName) {& {
				$ErrorActionPreference = "Continue"
				# resolve package name and version to a package
				$c = try {
					$REPOSITORY.GetPackage($n, $true, $true)
				} catch [Pog.RepositoryPackageNotFoundException] {
					$PSCmdlet.WriteError($_)
					continue
				}
				if (-not $Version) {
					# find latest version
					$c.GetLatestPackage()
				} else {
					try {
						$c.GetVersionPackage($Version, $true)
					} catch [Pog.RepositoryPackageVersionNotFoundException] {
						$PSCmdlet.WriteError($_)
						continue
					}
				}
			}}
		}

		foreach ($SrcPackage in $SrcPackages) {
			# see NOTES above for why we don't use a default parameter value
			$ResolvedTargetName = if ($TargetName) {$TargetName} else {$SrcPackage.PackageName}

			Write-Verbose "Validating the repository package before importing..."
			# this forces a manifest load on $SrcPackage
			if (-not (Confirm-PogRepositoryPackage $SrcPackage)) {
				throw "Validation of the repository package failed (see warnings above), not importing."
			}

			# don't load the manifest yet (may not be valid, will be loaded in ConfirmManifestOverwrite)
			$p = $PACKAGE_ROOTS.GetPackage($ResolvedTargetName, $TargetPackageRoot, $true, $false)

			if (-not (ConfirmManifestOverwrite $p $TargetPackageRoot $SrcPackage -Force:$Force)) {
				Write-Information "Skipping import of package '$($p.PackageName)'."
				continue
			}

			# import the package, replacing the previous manifest (and creating the directory if the package is new)
			$SrcPackage.ImportTo($p)

			Write-Information "Initialized '$($p.Path)' with package manifest '$($SrcPackage.PackageName)' (version '$($SrcPackage.Version)')."
			if ($PassThru) {
				# reload to remove the previous cached manifest
				# the imported manifest was validated, this should not throw
				$p.ReloadManifest()
				echo $p
			}
		}
	}
}

Export function Export-Pog {
	# .SYNOPSIS
	#	Exports shortcuts and commands from the package.
	# .DESCRIPTION
	#	Exports shortcuts from the package to the start menu, and commands to an internal Pog directory that's available on $env:PATH.
	[CmdletBinding(PositionalBinding = $false, DefaultParameterSetName = "PackageName")]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(Mandatory, Position = 0, ParameterSetName = "Package", ValueFromPipeline)]
			[Pog.ImportedPackage[]]
		$Package,
			### Name of the package to export. This is the target name, not necessarily the manifest app name.
			[Parameter(Mandatory, Position = 0, ParameterSetName = "PackageName", ValueFromPipeline)]
			[ArgumentCompleter([Pog.PSAttributes.ImportedPackageNameCompleter])]
			[string[]]
		$PackageName,
			### Export shortcuts to the systemwide start menu for all users, instead of the user-specific start menu.
			[switch]
		$Systemwide,
			### Return a [Pog.ImportedPackage] object with information about the package.
			[switch]
		$PassThru
	)

	begin {
		$StartMenuDir = if ($Systemwide) {
			[Pog.PathConfig]::StartMenuSystemExportDir
		} else {
			[Pog.PathConfig]::StartMenuUserExportDir
		}

		if (-not (Test-Path $StartMenuDir)) {
			$null = New-Item -Type Directory $StartMenuDir
		}
	}

	process {
		$Packages = if ($Package) {$Package} else {& {
			$ErrorActionPreference = "Continue"
			foreach($pn in $PackageName) {
				try {
					$PACKAGE_ROOTS.GetPackage($pn, $true, $true)
				} catch [Pog.ImportedPackageNotFoundException] {
					$PSCmdlet.WriteError($_)
				}
			}
		}}

		foreach ($p in $Packages) {
			foreach ($_ in $p.EnumerateExportedShortcuts()) {
				$TargetPath = Join-Path $StartMenuDir $_.Name
				if (Test-Path $TargetPath) {
					if ([Pog.FsUtils]::FileContentEqual((Get-Item $TargetPath), $_)) {
						Write-Verbose "Shortcut '$($_.BaseName)' is already exported from this package."
						continue
					}
					Write-Warning "Overwriting existing shortcut '$($_.BaseName)'..."
					Remove-Item -Force $TargetPath
				}
				Copy-Item $_ -Destination $TargetPath
				Write-Information "Exported shortcut '$($_.BaseName)' from '$($p.PackageName)'."
			}

			# TODO: check if $PATH_CONFIG.ExportedCommandDir is in PATH, and warn the user if it's not
			foreach ($Command in $p.EnumerateExportedCommands()) {
				$TargetPath = Join-Path $PATH_CONFIG.ExportedCommandDir $Command.Name
				$CmdName = $Command.BaseName

				if ($Command.FullName -eq [Pog.Native.Symlink]::GetLinkTarget($TargetPath, $false)) {
					Write-Verbose "Command '${CmdName}' is already exported from this package."
					continue
				}

				$MatchingCommands = ls $PATH_CONFIG.ExportedCommandDir -File -Filter ($CmdName + ".*") `
						| ? {$_.BaseName -eq $CmdName} # filter out files with a dot before the extension (e.g. `arm-none-eabi-ld.bfd.exe`)

				# there should not be more than 1, if we've done this checking correctly
				if (@($MatchingCommands).Count -gt 1) {
					Write-Warning "Pog developers fucked something up, and there are multiple colliding commands. Plz send bug report."
				}

				if (@($MatchingCommands).Count -gt 0) {
					Write-Warning "Overwriting existing command '${CmdName}'..."
					Remove-Item -Force $MatchingCommands
				}

				[Pog.Native.Symlink]::CreateSymbolicLink($TargetPath, $Command.FullName, $false)
				Write-Information "Exported command '${CmdName}' from '$($p.PackageName)'."
			}

			if ($PassThru) {
				echo $p
			}
		}
	}
}

# defined below
Export alias pog Invoke-Pog

# CmdletBinding is manually copied from Import-Pog, there doesn't seem any way to dynamically copy this like with dynamicparam
# TODO: rollback on error
Export function Invoke-Pog {
	# .SYNOPSIS
	#   Import, install, enable and export a package.
	# .DESCRIPTION
	#	Runs all four installation stages in order. All arguments passed to this cmdlet,
	#	except for the `-InstallOnly` switch, are forwarded to `Import-Pog`.
	[CmdletBinding(PositionalBinding = $false, DefaultParameterSetName = "Separate")]
	param(
			### Only import and install the package, do not enable and export.
			[switch]
		$InstallOnly,
			### Import, install and enable the package, do not export it.
			[switch]
		$NoExport
	)

	dynamicparam {
		$CopiedParams = Copy-CommandParameters (Get-Command Import-Pog)
		return $CopiedParams
	}

	begin {
		$Params = $CopiedParams.Extract($PSBoundParameters)

		# reuse PassThru parameter from Import-Pog for Enable-Pog
		$PassThru = [bool]$Params["PassThru"]

		$LogArgs = @{}
		if ($PSBoundParameters.ContainsKey("Verbose")) {$LogArgs["Verbose"] = $PSBoundParameters.Verbose}
		if ($PSBoundParameters.ContainsKey("Debug")) {$LogArgs["Debug"] = $PSBoundParameters.Debug}

		$null = $Params.Remove("PassThru")

		$SbAll =      {Import-Pog -PassThru @Params | Install-Pog -PassThru @LogArgs | Enable-Pog -PassThru @LogArgs | Export-Pog -PassThru:$PassThru @LogArgs}
		$SbNoExport = {Import-Pog -PassThru @Params | Install-Pog -PassThru @LogArgs | Enable-Pog -PassThru:$PassThru @LogArgs}
		$SbNoEnable = {Import-Pog -PassThru @Params | Install-Pog -PassThru:$PassThru @LogArgs}

		$Sb = if ($InstallOnly) {$SbNoEnable}
			elseif ($NoExport) {$SbNoExport}
			else {$SbAll}

		$sp = $Sb.GetSteppablePipeline()
		$sp.Begin($PSCmdlet)
	}

	process {
		$sp.Process($_)
	}

	end {
		$sp.End()
	}
}


Export function Show-PogManifestHash {
	# .SYNOPSIS
	#	Download resources for the given package and show SHA-256 hashes.
	# .DESCRIPTION
	#	Download all resources specified in the package manifest, store them in the download cache and show the SHA-256 hash.
	#	This cmdlet is useful for retrieving the archive hash when writing a package manifest.
	[CmdletBinding()]
	param(
			# ValueFromPipelineByPropertyName lets us avoid having to define a separate $Package parameter
			# TODO: add support for an array of package names, similarly to other commands

			### Name of the repository package to retrieve.
			[Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageNameCompleter])]
			[string]
		$PackageName,
			### Version of the package to retrieve. By default, the latest version is used.
			[Parameter(ValueFromPipelineByPropertyName)]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageVersionCompleter])]
			[Pog.PackageVersion]
		$Version,
			### Download files with low priority, which results in better network responsiveness
			### for other programs, but possibly slower download speed.
			[switch]
		$LowPriority
	)

	# TODO: add -PassThru to return structured object instead of Write-Host pretty-print

	process {
		$c = try {
			$REPOSITORY.GetPackage($PackageName, $true, $true)
		} catch [Pog.RepositoryPackageNotFoundException] {
			$PSCmdlet.WriteError($_)
			return
		}
		# get the resolved name, so that the casing is correct
		$PackageName = $c.PackageName

		$p = if (-not $Version) {
			# find latest version
			$c.GetLatestPackage()
		} else {
			$c.GetVersionPackage($Version, $true)
		}

        # this forces a manifest load on $p
		if (-not (Confirm-PogRepositoryPackage $p)) {
			throw "Validation of the repository package failed (see warnings above)."
		}

		if (-not $p.Manifest.Raw.ContainsKey("Install")) {
			Write-Information "Package '$($p.PackageName)' does not have an Install block."
			continue
		}

		$InternalArgs = @{
			AllowOverwrite = $true # not used
			DownloadLowPriority = [bool]$LowPriority
		}

		Invoke-Container GetInstallHash $p -InternalArguments $InternalArgs
	}
}

<# Ad-hoc template format used to create default manifests in the following 2 functions. #>
function RenderTemplate($SrcPath, $DestinationPath, [Hashtable]$TemplateData) {
	$Template = Get-Content -Raw $SrcPath
	foreach ($Entry in $TemplateData.GetEnumerator()) {
		$Template = $Template.Replace("'{{$($Entry.Key)}}'", "'" + $Entry.Value.Replace("'", "''") + "'")
	}
	$null = New-Item -Path $DestinationPath -Value $Template
}

Export function New-PogPackage {
	[CmdletBinding()]
	[OutputType([Pog.RepositoryPackage])]
	param(
			[Parameter(Mandatory)]
			[Pog.Verify+PackageName()]
			[string]
		$PackageName,
			[Parameter(Mandatory)]
			[Pog.PackageVersion]
		$Version,
			[switch]
		$Templated
	)

	begin {
		$c = $REPOSITORY.GetPackage($PackageName, $true, $false)

		if ($c.Exists) {
            throw "Package '$($c.PackageName)' already exists in the repository at '$($c.Path)'.'"
        }

		$null = New-Item -Type Directory $c.Path
        if ($Templated) {
			$null = New-Item -Type Directory $c.TemplateDirPath
        }

		# only get the package after the parent is created, otherwise it would always default to a non-templated package
		$p = $c.GetVersionPackage($Version, $false)

		$TemplateData = @{NAME = $p.PackageName; VERSION = $p.Version.ToString()}
		if ($Templated) {
			# template dir is already created above
			RenderTemplate "$PSScriptRoot\resources\manifest_templates\repository_templated.psd1" $p.TemplatePath $TemplateData
			RenderTemplate "$PSScriptRoot\resources\manifest_templates\repository_templated_data.psd1" $p.ManifestPath $TemplateData
		} else {
			# create manifest dir for version
			$null = New-Item -Type Directory $p.Path
			RenderTemplate "$PSScriptRoot\resources\manifest_templates\repository_direct.psd1" $p.ManifestPath $TemplateData
		}

		return $p
	}
}

Export function New-PogImportedPackage {
	[CmdletBinding()]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(Mandatory)]
			[Pog.Verify+PackageName()]
			[string]
		$PackageName,
			[ArgumentCompleter([Pog.PSAttributes.ValidPackageRootPathCompleter])]
			[string]
		$PackageRoot
	)

	begin {
		$PackageRoot = if (-not $PackageRoot) {
			$PACKAGE_ROOTS.DefaultPackageRoot
		} else {
			try {$PACKAGE_ROOTS.ResolveValidPackageRoot($PackageRoot)}
			catch [Pog.PackageRootNotValidException] {
				$PSCmdlet.ThrowTerminatingError($_)
			}
		}

		$p = $PACKAGE_ROOTS.GetPackage($PackageName, $PackageRoot, $false, $false)
		if ($p.Exists) {
			throw "Package already exists: $($p.Path)"
		}

		# create the package dir
		$null = New-Item -Type Directory $p.Path
		RenderTemplate "$PSScriptRoot\resources\manifest_templates\imported.psd1" $p.ManifestPath @{NAME = $p.PackageName}

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
Export function Update-PogManifest {
	# .SYNOPSIS
	#	Generate new manifests in the package repository for the given package manifest generator.
	[CmdletBinding()]
	[OutputType([Pog.RepositoryPackage])]
	param(
			### Name of the manifest generator for which to generate new manifests.
			[Parameter(ValueFromPipeline)]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageGeneratorNameCompleter])]
			[string]
		$PackageName,
			# we only use -Version to match against retrieved versions, no need to parse

			### List of versions to generate/update manifests for.
			[string[]]
		$Version,
			### Regenerate even existing manifests. By default, only manifests for versions that
			### do not currently exist in the repository are generated.
			[switch]
		$Force,
			### Only retrieve and list versions, do not generate manifests.
			[switch]
		$ListOnly
	)

	begin {
		if (-not $PackageName -and -not $MyInvocation.ExpectingInput) {
			if ($null -ne $Version) {
				throw "$($MyInvocation.InvocationName): `$Version must not be passed when `$PackageName is not set" +`
						" (it does not make much sense to select the same version for all generated packages)."
			}
			$GeneratorNames = $GENERATOR_REPOSITORY.EnumerateGeneratorNames()
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
		if (-not $PackageName) {
			return # ignore, already processed in begin block
		}

		$c = $REPOSITORY.GetPackage($PackageName, $true, $false)
		$PackageName = $c.PackageName
		$GenDir = Join-Path $PATH_CONFIG.ManifestGeneratorDir $PackageName

		if (-not (Test-Path -Type Container $GenDir)) {
			& {
				$ErrorActionPreference = "Continue"
				try {
					throw "Package generator for '$PackageName' does not exist, expected path: $GenDir"
				} catch {
					$PSCmdlet.WriteError($_) # making your own error records is hard :(
				}
			}
			return
		}

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
				return $GeneratedVersions | % {$c.GetVersionPackage($_.Version, $false)}
			}

			# generate manifest for each version
			$GeneratedVersions | % {
				$p = $c.GetVersionPackage($_.Version, $false)
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
						# TODO: also check if the manifest file itself is generated, or possibly run full validation (Confirm-PogRepositoryPackage)
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

Export function Confirm-PogRepositoryPackage {
	# .SYNOPSIS
	#	Validates a repository package.
	[CmdletBinding(DefaultParameterSetName="Separate")]
	param(
			# TODO: add support for an array of packages/package names, similarly to other commands
			[Parameter(Mandatory, Position=0, ValueFromPipeline, ParameterSetName="Package")]
			[Pog.RepositoryPackage]
		$Package,
			[Parameter(Mandatory, Position=0, ValueFromPipeline, ParameterSetName="Separate")]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageNameCompleter])]
			[string]
		$PackageName,
			[Parameter(Position=1, ParameterSetName="Separate")]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageVersionCompleter])]
			[Pog.PackageVersion]
		$Version
	)

	begin {
		# FIXME: missing -Version parameter check like in other commands, so this works (but shouldn't):
		#  "7zip", "git" | Confirm-PogRepositoryPackage -ErrorAction Continue -Version 1

		# rename to avoid shadowing
		$VersionParam = $Version

		$NoIssues = $true
		function AddIssue([string]$IssueMsg) {
			Set-Variable NoIssues $false -Scope 1
			Write-Warning $IssueMsg.Replace("`n", "`n         ")
		}

		function ValidateManifestDirStructure($Path, $PackageInfoStr) {
			foreach ($f in (Get-ChildItem $Path)) {
				if ($f.Name -notin "pog.psd1", ".pog") {
					AddIssue ("Manifest directory for $PackageInfoStr contains extra file/directory '$($f.Name)' at '$f'." `
							+ " Each manifest directory must only contain a pog.psd1 manifest file and an optional .pog directory for extra files.")
				}
			}

			$ExtraFileDir = Get-Item "$Path\.pog" -ErrorAction Ignore
			if ($ExtraFileDir -and $ExtraFileDir -isnot [System.IO.DirectoryInfo]) {
				AddIssue ("'$ExtraFileDir' should be a directory, not a file, in manifest directory for $PackageInfoStr.")
			}
		}
	}

	process {
		if ($Package) {
			$VersionPackages = $Package
		} else {
			try {
				$c = $REPOSITORY.GetPackage($PackageName, $true, $true)
			} catch [Pog.RepositoryPackageNotFoundException] {
				$NoIssues = $false
				$PSCmdlet.WriteError($_)
				return
			}
			$VersionStr = if ($VersionParam) {", version '$VersionParam'"} else {" (all versions)"}
			Write-Verbose "Validating package '$PackageName'$VersionStr from local repository..."

			# TODO: validate that package has at least one version defined
			if (-not $VersionParam) {
				$VersionPackages = $c.Enumerate()

				if ($c.IsTemplated) {
					$ExtraFiles = Get-ChildItem -File $c.Path | ? {$_.Name -notlike "*.psd1"}
					if ($ExtraFiles) {
						AddIssue "Package '$PackageName' has incorrect structure; root contains extra files (only .psd1 should be present): $ExtraFiles"
					}

					$ExtraDirs = Get-ChildItem -Directory $c.Path | ? {$_.FullName -ne $c.TemplateDirPath}
					if ($ExtraDirs) {
						AddIssue "Package '$PackageName' has incorrect structure; root contains invalid extra directories: $ExtraDirs"
					}

					ValidateManifestDirStructure $c.TemplateDirPath "'$($c.PackageName)'"
				} else {
					$Files = Get-ChildItem -File $c.Path
					if ($Files) {
						AddIssue "Package '$PackageName' has incorrect structure; root contains the following files (only version directories should be present): $Files"
					}
				}

			} else {
				try {
					$VersionPackages = $c.GetVersionPackage($VersionParam, $true)
				} catch [Pog.RepositoryPackageVersionNotFoundException] {
					$NoIssues = $false
					$PSCmdlet.WriteError($_)
					return
				}
			}
		}

		foreach ($p in $VersionPackages) {
			if ($p -is [Pog.DirectRepositoryPackage]) {
				ValidateManifestDirStructure $p.Path "'$($p.PackageName)', version '$($p.Version)'"
			}

			try {
				$p.ReloadManifest()
			} catch [Pog.PackageManifestNotFoundException] {
				AddIssue "Could not find manifest '$($p.PackageName)', version '$($p.Version)'. Searched path: $($_.FileName)"
				return
			} catch [Pog.PackageManifestParseException] {
				AddIssue $_
				return
			} catch {
				AddIssue $_
				return
			}

			try {
				Confirm-Manifest $p.Manifest $p.PackageName $p.Version -IsRepositoryManifest
			} catch {
				AddIssue ("Validation of package manifest '$($p.PackageName)', version '$($p.Version)' from local repository failed." +`
						"`nPath: $($p.ManifestPath)" +`
						"`n" + $_.ToString().Replace("`t", "     - "))
			}
		}
	}

	end {
		return $NoIssues
	}
}

# TODO: expand to really check whole package, not just manifest, then update Install-Pog and Enable-Pog to use this instead of Confirm-Manifest
#  when switched, figure out what to do about the forced manifest reload (we do not want to load the manifest multiple times)
Export function Confirm-PogPackage {
	# .SYNOPSIS
	#	Checks that the manifest of an imported package is valid.
	[CmdletBinding(DefaultParameterSetName="Name")]
	param(
			[Parameter(Mandatory, Position=0, ValueFromPipeline, ParameterSetName="Package")]
			[Pog.ImportedPackage[]]
		$Package,
			[Parameter(Mandatory, Position=0, ValueFromPipeline, ParameterSetName="Name")]
			[ArgumentCompleter([Pog.PSAttributes.ImportedPackageNameCompleter])]
			[string[]]
		$PackageName
	)

	begin {
		$NoIssues = $true
		function AddIssue($IssueMsg) {
			Set-Variable NoIssues $false -Scope 1
			Write-Warning $IssueMsg.Replace("`n", "`n         ")
		}
	}

	process {
		$Packages = if ($Package) {
			foreach ($p in $Package) {
				try {
					# re-read manifest to have it up-to-date and revalidated
					$p.ReloadManifest()
					$p
				} catch {
					# unwrap the actual exception, otherwise we would get a MethodInvocationException instead,
					#  which has a less readable error message
					AddIssue $_.Exception.InnerException.Message
				}
			}
		} else {& {
			$ErrorActionPreference = "Continue"
			foreach($pn in $PackageName) {
				try {
					$PACKAGE_ROOTS.GetPackage($pn, $true, $true)
				} catch [Pog.ImportedPackageNotFoundException] {
					Set-Variable NoIssues $false -Scope 1
					$PSCmdlet.WriteError($_)
				} catch {
					# unwrap the actual exception, otherwise we would get a MethodInvocationException instead,
					#  which has a less readable error message
					AddIssue $_.Exception.InnerException.Message
				}
			}
		}}

		foreach ($p in $Packages) {
			Write-Verbose "Validating imported package manifest '$($p.PackageName)' at '$($p.ManifestPath)'..."
			try {
				Confirm-Manifest $p.Manifest
			} catch {
				AddIssue "Validation of imported package manifest '$($p.PackageName)' at '$($p.ManifestPath)' failed: $_"
				return
			}
		}
	}

	end {
		return $NoIssues
	}
}