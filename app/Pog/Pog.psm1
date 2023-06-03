# Requires -Version 7
using module .\Paths.psm1
using module .\lib\Utils.psm1
using module .\Common.psm1
using module .\Confirmations.psm1
using module .\lib\Copy-CommandParameters.psm1
. $PSScriptRoot\lib\header.ps1


Export function Get-PogRepositoryPackage {
	[CmdletBinding(DefaultParameterSetName="Version")]
	[OutputType([Pog.RepositoryPackage])]
	param(
			[Parameter(Position=0, ValueFromPipeline, ValueFromPipelineByPropertyName)]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageNameCompleter])]
			[string[]]
		$PackageName,
			# TODO: figure out how to remove this parameter when -PackageName is an array
			[Parameter(Position=1, ParameterSetName="Version")]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageVersionCompleter])]
			[Pog.PackageVersion]
		$Version,
			[Parameter(ParameterSetName="AllVersions")]
			[switch]
		$AllVersions
	)

	begin {
		if ($Version) {
			if ($MyInvocation.ExpectingInput) {throw "-Version must not be passed together with pipeline input."}
			if (-not $PackageName) {throw "-Version must not be passed without also passing -PackageName."}
			if (@($PackageName).Count -gt 1) {throw "-Version must not be passed when -PackageName contains multiple package names."}

			return $REPOSITORY.GetPackage($PackageName, $true, $true).GetVersionPackage($Version, $true)
		}

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
	[CmdletBinding()]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(ValueFromPipeline, ValueFromPipelineByPropertyName)]
			[ArgumentCompleter([Pog.PSAttributes.ImportedPackageNameCompleter])]
			[string[]]
		$PackageName,
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

<# Remove cached package archives older than the provided date. #>
Export function Clear-PogDownloadCache {
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
			### If set, and some version of the package is already installed, prompt before overwriting
			### with the current version according to the manifest.
			[switch]
		$Confirm,
			### If set, files are downloaded with low priority, which results in better network responsiveness
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

function ClearPreviousPackageDir($p, $TargetPackageRoot, $SrcPackage, [switch]$Force) {
	$OrigManifest = $null
	try {
		# try to load the (possibly) existing manifest
		$p.ReloadManifest()
		$OrigManifest = $p.Manifest
	} catch [System.IO.DirectoryNotFoundException] {
		# the package does not exist, create the directory and return
		$null = New-Item -Type Directory $p.Path
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

	[Pog.PathConfig+PackagePaths]::ManifestCleanupPaths | % {Join-Path $p.Path $_} | ? {Test-Path $_} | Remove-Item -Recurse
	return $true
}

# TODO: allow wildcards in PackageName and Version arguments for commands where it makes sense
Export function Import-Pog {
	# .SYNOPSIS
	#	Imports a package manifest from the repository.
	[CmdletBinding(PositionalBinding = $false, DefaultParameterSetName = "Separate")]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(Mandatory, Position = 0, ParameterSetName = "RepositoryPackage", ValueFromPipeline)]
			[Pog.RepositoryPackage[]]
		$Package,
			[Parameter(Mandatory, ParameterSetName = "PackageName", ValueFromPipeline)]
			[Parameter(Mandatory, Position = 0, ParameterSetName = "Separate")]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageNameCompleter])]
			[string[]]
		$PackageName,
			[Parameter(Position = 1, ParameterSetName = "Separate")]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageVersionCompleter])]
			[Pog.PackageVersion]
		$Version,
			[Parameter(ParameterSetName = "Separate")]
			[ArgumentCompleter([Pog.PSAttributes.ImportedPackageNameCompleter])]
			[string]
		$TargetName,
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

			# don't load the manifest yet (may not be valid, will be loaded in ClearPreviousPackageDir)
			$p = $PACKAGE_ROOTS.GetPackage($ResolvedTargetName, $TargetPackageRoot, $true, $false)

			# ensure $TargetPath exists and there's no package manifest
			if (-not (ClearPreviousPackageDir $p $TargetPackageRoot $SrcPackage -Force:$Force)) {
				Write-Information "Skipping import of package '$($p.PackageName)'."
				continue
			}

			# copy the new package files from the repository
			ls $SrcPackage.Path | Copy-Item -Recurse -Destination $p.Path
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
			### Export shortcuts to the systemwide start menu.
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
					if ([Pog.PathUtils]::FileContentEqual((gi $TargetPath), $_)) {
						Write-Verbose "Shortcut '$($_.BaseName)' is already exported from this package."
						continue
					}
					Write-Warning "Overwriting existing shortcut '$($_.BaseName)'..."
					Remove-Item -Force $TargetPath
				}
				Copy-Item $_ -Destination $TargetPath
				Write-Verbose "Exported shortcut '$($_.BaseName)' from '$($p.PackageName)'."
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
				Write-Verbose "Exported command '${CmdName}' from '$($p.PackageName)'."
			}

			if ($PassThru) {
				echo $p
			}
		}
	}
}

# CmdletBinding is manually copied from Import-Pog, there doesn't seem any way to dynamically copy this like with dynamicparam
# TODO: rollback on error
Export function Invoke-Pog {
	# .SYNOPSIS
	#   Import, install and enable a package.
	[CmdletBinding(PositionalBinding = $false, DefaultParameterSetName = "Separate")]
	param([switch]$InstallOnly)
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

		$SbWithEnable = {Import-Pog -PassThru @Params | Install-Pog -PassThru @LogArgs | Enable-Pog -PassThru @LogArgs | Export-Pog -PassThru:$PassThru @LogArgs}
		$SbNoEnable = {Import-Pog -PassThru @Params | Install-Pog -PassThru:$PassThru @LogArgs}

		$sp = ($InstallOnly ? $SbNoEnable : $SbWithEnable).GetSteppablePipeline()
		$sp.Begin($PSCmdlet)
	}

	process {
		$sp.Process($_)
	}

	end {
		$sp.End()
	}
}

New-Alias pog Invoke-Pog
Export-ModuleMember -Alias pog


Export function Show-PogManifestHash {
	# .SYNOPSIS
	# Download all resources specified in the package manifest, store them in the download cache and show the SHA-256 hash.
	# This cmdlet is useful for retrieving the archive hash when writing a package manifest.
	[CmdletBinding()]
	param(
			# ValueFromPipelineByPropertyName lets us avoid having to define a separate $Package parameter
			# TODO: add support for an array of package names, similarly to other commands
			[Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageNameCompleter])]
			[string]
		$PackageName,
			[Parameter(ValueFromPipelineByPropertyName)]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageVersionCompleter])]
			[Pog.PackageVersion]
		$Version,
			### If set, files are downloaded with low priority, which results in better network responsiveness
			### for other programs, but possibly slower download speed.
			[switch]
		$LowPriority
	)
	# TODO: -PassThru to return structured object instead of Write-Host pretty-print

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

Export function New-PogManifest {
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
		$p = $REPOSITORY.GetPackage($PackageName, $true, $false).GetVersionPackage($Version, $false)

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

Export function New-PogImportedPackage {
	[CmdletBinding()]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(Mandatory)]
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

		$PackageDirectory = New-Item -Type Directory $p.Path
		try {
			Copy-Item $PSScriptRoot\resources\direct_manifest_template.psd1 $p.ManifestPath
			$p.ReloadManifest()
			return $p
		} catch {
			Remove-Item -Recurse -Force $PackageDirectory
			throw
		}
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
Export function Update-PogManifest {
	[CmdletBinding()]
	[OutputType([Pog.RepositoryPackage])]
	param(
			[Parameter(ValueFromPipeline)]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageGeneratorNameCompleter])]
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
			$VersionPackages = if (-not $VersionParam) {
				$Files = ls -File $c.Path
				if ($Files) {
					AddIssue "Package '$PackageName' has incorrect structure; root contains following files (only version directories should be present): $Files"
					return
				}
				$c.Enumerate()
			} else {
				try {
					$c.GetVersionPackage($VersionParam, $true)
				} catch [Pog.RepositoryPackageVersionNotFoundException] {
					$NoIssues = $false
					$PSCmdlet.WriteError($_)
					return
				}
			}
		}

		foreach ($p in $VersionPackages) {
			foreach ($f in ls $p.Path) {
				if ($f.Name -notin "pog.psd1", ".pog") {
					AddIssue ("Manifest directory for '$($p.PackageName)', version '$($p.Version)' contains extra file/directory '$($f.Name)' at '$f'." `
							+ " Each manifest directory must only contain a pog.psd1 manifest file and an optional .pog directory for extra files.")
				}
			}

			$ExtraFileDir = Get-Item "$p\.pog" -ErrorAction Ignore
			if ($ExtraFileDir -and $ExtraFileDir -isnot [System.IO.DirectoryInfo]) {
				AddIssue ("'$ExtraFileDir' should be a directory, not a file, in manifest directory for '$($p.PackageName)', version '$($p.Version)'.")
			}

			if (-not $p.ManifestExists) {
				AddIssue "Could not find manifest '$($p.PackageName)', version '$($p.Version)'. Searched path: $($p.ManifestPath)"
				return
			}

			try {
				$p.ReloadManifest()
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
