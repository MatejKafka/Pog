# Requires -Version 7
using module ..\..\Paths.psm1
using module ..\..\lib\Utils.psm1
using module ..\container_lib\Environment.psm1
using module ..\container_lib\Confirmations.psm1
using module .\command_generator\SubstituteExe.psm1
. $PSScriptRoot\..\..\lib\header.ps1


Export-ModuleMember -Function Confirm-Action

# not sure if we should expose this, as packages really shouldn't need to use admin privilege
# currently, this is used by Notepad++ to optionally redirect Notepad to Notepad++ in Registry
Export-ModuleMember -Function Assert-Admin

# also not sure about this, PowerShell (private package) uses it to set PSModulePath
Export-ModuleMember -Function Add-EnvVar, Set-EnvVar



<# This function is called after the container setup is finished to run the Enable script. #>
Export function __main {
	param($Manifest, $PackageArguments)

	# set of all shortcuts that were not "refreshed" during this Enable call
	# starts with all shortcuts found in package, and each time Export-Shortcut is called, it is removed
	# before end of Enable, all shortcuts still in this set are deleted
	$script:StaleShortcuts = [System.Collections.Generic.HashSet[string]]::new()
	ls -File -Filter "./*.lnk" | % {[void]$script:StaleShortcuts.Add($_.BaseName)}
	Write-Debug "Listed original shortcuts."

	# invoke the scriptblock
	& $Manifest.Enable @PackageArguments
}

<# This function is called after the Enable script finishes. #>
Export function __cleanup {
	# remove stale shortcuts
	if ($script:StaleShortcuts.Count -gt 0) {
		Write-Verbose "Removing stale shortcuts..."
	}
	$script:StaleShortcuts | % {
		Remove-Item ("./" + $_ + ".lnk")
		Write-Verbose "Removed stale shortcut '$_'."
	}
}



function Assert-ParentDirectory {
	param(
			[Parameter(Mandatory)]
		$Path
	)

	$Parent = Split-Path -Parent $Path
	if (-not (Test-Path $Parent)) {
		$null = New-Item -ItemType Directory $Parent
	}
}

Export function Merge-Directories {
	param(
			[Parameter(Mandatory)]
		$SrcDir,
			[Parameter(Mandatory)]
		$TargetDir,
			# when set, target will be left without overwriting in case of collision
			[switch]
		$PreferTarget
	)

	ls -Force $SrcDir | % {
		$Target = $TargetDir + "\" + $_.Name
		if (Test-Path $Target) {
			if ($PreferTarget) {return}  # skip
			# overwrite with new version
			Remove-Item -Recurse $Target
		}
		Move-Item $_ $Target
	}
	Remove-Item -Recurse $SrcDir
}

function Set-Symlink {
	param(
			# This path must be either non-existent, or already a symlink.
			[Parameter(Mandatory)]
		$LinkPath,
			# Path to target, must exist.
			[Parameter(Mandatory)]
		$TargetPath
	)

	# note that this returns the provider path (real FS path), not the PSDrive path
	$LinkAbsPath = Resolve-VirtualPath $LinkPath
	# this one must exist, FullName is also a real FS path
	$Target = Get-Item $TargetPath

	[string]$TargetStr = if ([System.IO.Path]::IsPathRooted($LinkPath) -or [System.IO.Path]::IsPathRooted($TargetPath)) {
		# one of the paths is rooted, use absolute path for symlink
		[string]$Target
	} else {
		# get relative path from $LinkPath to $TargetPath for symlink
		# use parent of $LinkPath, as relative symlinks are resolved from parent dir
		[IO.Path]::GetRelativePath((Split-Path $LinkAbsPath), $Target)
	}

	if (Test-Path $LinkAbsPath) {
		$Item = Get-Item $LinkAbsPath
		if ($Item.Target -eq $TargetStr) {
			return $null # we already have a correct symlink
		}

		# not a correct item, delete and recreate
		Remove-Item -Recurse $Item
	} else {
		Assert-ParentDirectory $LinkAbsPath
	}

	Write-Debug "Creating symlink from '$LinkAbsPath' with target '$TargetStr'."
	# New-Item -Type SymbolicLink has a dumb issue with relative paths, so we use the .NET methods instead
	#  https://github.com/PowerShell/PowerShell/issues/15235
	if ($Target.PSIsContainer) {
		return [System.IO.Directory]::CreateSymbolicLink($LinkAbsPath, $TargetStr)
	} else {
		return [System.IO.File]::CreateSymbolicLink($LinkAbsPath, $TargetStr)
	}
}

enum ItemType {File; Directory}
<#
	What Set-SymlinkedPath should do:
	if target exists:
		switch source state:
			does not exist:
				- create symlink to target, leave target as-is
			is symlink to target:
				- nothing to do, already set
			is symlink somewhere else:
				- delete, replace
			exists, not symlink:
				- if -Merge was passed, merge source dir to target
				- remove source, replace with symlink
	else:
		switch source state:
			is symlink:
				- delete source
				- create empty target
			- move source to target
			- create symlink at source
		else:

#>
Export function Set-SymlinkedPath {
	param(
			[Parameter(Mandatory)]
		$OriginalPath,
			[Parameter(Mandatory)]
		$TargetPath,
			[switch]
		$Merge,
			# if target is supposed to be 'File' or 'Directory'
			[Parameter(Mandatory)]
			[Alias("Type")]
			[ItemType]
		$ItemType
	)

	begin {
		if ($Merge -and $ItemType -ne [ItemType]::Directory) {
			throw "'-Merge' switch for Set-SymlinkedPath may only be passed when '-ItemType Directory' is set."
		}

		$TestType = switch ($ItemType) {File {"Leaf"}; Directory {"Container"}}

		# if orig exists and doesn't match expected item type
		if ((Test-Path $OriginalPath) -and ($null -eq (Get-Item $OriginalPath).LinkType) `
				-and -not (Test-Path -Type $TestType $OriginalPath)) {
			$OppositeType = switch ($ItemType) {File {[ItemType]::Directory}; Directory {[ItemType]::File}}
			throw "Cannot symlink source path '$OriginalPath' to '$TargetPath' - expected '$ItemType', found '$OppositeType'."
		}

		# OriginalPath is either symlink or matches item type

		if ((Test-Path $TargetPath) -and -not (Test-Path -Type $TestType $TargetPath)) {
			Write-Warning "Item '$TargetPath' exists, but it's not '$ItemType'. Replacing..."
			# mismatch between requested and real target type
			Remove-Item -Recurse $TargetPath
		}

		# TargetPath matches item type

		if (-not (Test-Path $TargetPath)) {
			Assert-ParentDirectory $TargetPath
			# $OriginalPath exists and it's not a symlink
			if ((Test-Path $OriginalPath) -and (Get-Item $OriginalPath).Target -eq $null) {
				# TODO: check if $OriginalPath is being used by another process; block if it is so
				# move it to target and then create symlink
				Move-Item $OriginalPath $TargetPath
			} else {
				$null = New-Item $TargetPath -ItemType $ItemType
			}
		} elseif ($Merge -and $null -eq (Get-Item $OriginalPath).LinkType) {
			Write-Information "Merging directory $OriginalPath to $TargetPath..."
			Merge-Directories $OriginalPath $TargetPath
		}

		$result = Set-Symlink $OriginalPath $TargetPath
		if ($null -eq $result) {
			Write-Verbose "Symlink already exists and matches requested target: '$OriginalPath'."
		} else {
			Write-Information "Created symlink from '$OriginalPath' to '$TargetPath'."
		}
	}
}


<# Ensures that given directory path exists. #>
Export function Assert-Directory {
	param([Parameter(Mandatory)]$Path)

	if (Test-Path -Type Container $Path) {
		Write-Verbose "Directory '$Path' already exists."
		return
	}
	if (Test-Path $Path) {
		throw "Path '$Path' already exists, but it's not a directory."
	}
	$null = New-Item -ItemType Directory $Path
	Write-Information "Created directory '$Path'."
}


<# Ensures that given file exists. #>
Export function Assert-File {
	param(
			[Parameter(Mandatory)]
			[string]
		$Path,
			# if file does not exist, use output of this script block to populate it
			# file is left empty if this is not passed
			[ScriptBlock]
		$DefaultContent = {param($File)},
			# if file does exist and this is passed, the script block is ran with reference to the file
			# NOTE: you have to save the output yourself (this was deemed more
			#  robust and often more efficient solution than just returning the desired new content)
			# return $true if something was changed, $false if original content was kept
			[ValidateScript({
				if ($_.GetType() -eq [scriptblock]) {return $true}
				if ($_.GetType() -eq [string]) {
					if (Test-Path -Type Leaf $_) {return $true}
					throw "-ContentUpdater is a path string, but it doesn't point to an existing PowerShell script file: '${_}'"
				}
				throw "-ContentUpdater must be either script block, or path to a PowerShell script file, got '$($_.GetType())'."
			})]
		$ContentUpdater = $null
	)

	if (Test-Path -Type Leaf $Path) {
		if ($null -eq $ContentUpdater) {
			Write-Verbose "File '$Path' already exists."
			return
		}

		$File = Get-Item $Path
		$null = & $ContentUpdater $File

		$WasChanged = $File.LastWriteTime -ne (Get-Item $Path).LastWriteTime
		if ($WasChanged) {
			Write-Information "File '$Path' updated."
			Write-Debug ("^ For manifest writers: last write time of the file changed during " +`
					"-ContentUpdater execution. If you don't think it should have changed " +`
					"and the file appears to be the same, " +`
					"check for differences in whitespace (especially \r\n vs \n).")
		} else {
			Write-Verbose "File '$Path' already exists with correct content."
		}
		return
	}

	if (Test-Path $Path) {
		# TODO: think this through; maybe it would be better to unconditionally overwrite it
		throw "Path '$Path' already exists, but it's not a file."
	}

	Assert-ParentDirectory $Path
	# create new file with default content
	# the generator script $DefaultContent can either create and populate the file directly,
	#  or just return the desired content and we'll create it ourselves
	# the first option is supported, because some apps have a builtin way to generate a default config directly
	$NewContent = & $DefaultContent (Resolve-VirtualPath $Path)
	if (-not (Test-Path $Path)) {
		# -NoNewline doesn't skip just the trailing newline, but all newlines;
		#  so we add the newlines between manually and use -NoNewline to avoid the trailing newline
		$NewContent | Join-String -Separator "`n" | Set-Content $Path -NoNewline
	}

	Write-Information "Created file '$Path'."
}


Export function Export-Shortcut {
	param(
			[Parameter(Mandatory)]
			[string]
		$ShortcutName,
			[Parameter(Mandatory)]
			[string]
		$TargetPath,
			[Alias("ArgumentList")]
		$Arguments,
		$WorkingDirectory,
			[switch]
		$StartMaximized,
			[switch]
		$StartMinimized,
			[Alias("Icon")]
		$IconPath,
		$Description
	)

	# FIXME: switch to a single WindowStyle argument
	if ($StartMaximized -and $StartMinimized) {
		throw "Export-Shortcut: -StartMaximized and -StartMinimized switch parameters must not be passed together."
	}

	# this shortcut was refreshed, not stale, remove it
	# noop when not present
	$null = $StaleShortcuts.Remove($ShortcutName)

	# shortcut takes all the arguments as a single string, so we need to quote it ourselves
	# unfortunately, I don't believe there's an universal way that will work with all programs,
	#  but this way of quoting and escaping nested quotes should work correctly for most programs:
	#  https://stackoverflow.com/a/15262019
	# fortunately, as quotes aren't a valid char for file/directory names, we shouldn't have to escape them too often
	$Arguments = $Arguments `
			| % {[string]$_} `
			| % {if ($_.Contains(" ") -or $_.Contains("`t")) {'"' + ($_ -replace '"', '"""') + '"'} else {$_}} `
			| Join-String -Separator " "

	$Shell = New-Object -ComObject "WScript.Shell"
	# Shell object has different CWD, have to resolve all paths
	$ShortcutPath = Resolve-VirtualPath "./$ShortcutName.lnk"

	$Target = if ($TargetPath.Contains("/") -or $TargetPath.Contains("\")) {
		# assume the target is a path
		Resolve-Path $TargetPath
	} else {
		# assume the target is a command in env:PATH
		$Cmd = Get-Command -CommandType Application $TargetPath -TotalCount 1 -ErrorAction SilentlyContinue
		if ($null -eq $Cmd) {
			throw "Cannot create shortcut to command '$TargetPath', as no such command is known by the system (present in env:PATH)."
		}
		Resolve-Path $Cmd.Source
	}

	Write-Debug "Resolved shortcut target: $Target"

	$WorkingDirectory = if ($WorkingDirectory -eq $null) {
		Resolve-Path (Split-Path $Target)
	} else {
		Resolve-Path $WorkingDirectory
	}

	if ($IconPath -eq $null) {
		$IconPath = $Target
	}
	$IconPath = Resolve-Path $IconPath

	# support copying icon from another .lnk
	if (".lnk" -eq (Split-Path -Extension $IconPath)) {
		$Icon = $Shell.CreateShortcut($IconPath).IconLocation
	} else {
		# icon index 0 = first icon in the file
		$Icon = [string]$IconPath + ",0"
	}

	$WinStyle = if ($StartMaximized) {3} elseif ($StartMinimized) {7} else {1}

	if ($null -eq $Description) {
		$Description = [string](Split-Path -LeafBase $TargetPath)
	}


	$S = $Shell.CreateShortcut($ShortcutPath)

	if ((Test-Path $ShortcutPath) `
			-and $S.TargetPath -eq $Target `
			-and $S.Arguments -eq $Arguments `
			-and $S.WorkingDirectory -eq $WorkingDirectory `
			-and $S.WindowStyle -eq $WinStyle `
			-and $S.IconLocation -eq $Icon `
			-and $S.Description -eq $Description) {
		Write-Verbose "Shortcut '$ShortcutName' is already configured."
		return
	}

	if (Test-Path $ShortcutPath) {
		Write-Verbose "Shortcut at '$ShortcutPath' already exists, reusing it..."
	}

	$S.TargetPath = $Target
	$S.Arguments = $Arguments
	$S.WorkingDirectory = $WorkingDirectory
	$S.WindowStyle = $WinStyle
	$S.IconLocation = $Icon
	$S.Description = $Description

	$S.Save()
	Write-Information "Set up a shortcut called '$ShortcutName' (target: '$TargetPath')."
}

Export function Export-Command {
	param(
			[Parameter(Mandatory)]
		$CmdName,
			[Parameter(Mandatory)]
		$ExePath,
			[switch]
		$SetWorkingDirectory,
			[switch]
		$NoSymlink
	)

	# TODO: check if BIN_DIR is in PATH, and warn the user if it's not
	#  this TODO might be obsolete if we move to a per-package store of exported commands with separate copy mechanism

	if (-not (Test-Path $ExePath)) {
		throw "Cannot register command '$CmdName', provided target '$ExePath' does not exist."
	}
	if (-not (Test-Path -Type Leaf $ExePath)) {
		throw "Cannot register command '$CmdName', provided target '$ExePath' exists, but it's not a file."
	}

	$ExePath = Resolve-Path $ExePath


	$UseSymlink = -not ($SetWorkingDirectory -or $NoSymlink)
	$LinkExt = if ($UseSymlink) {Split-Path -Extension $ExePath} else {".exe"}
	$LinkPath = Join-Path $script:BIN_DIR ($CmdName + $LinkExt)

	if (Test-Path -Type Leaf $LinkPath) {
		$Item = Get-Item $LinkPath
		if ($Item.Target -eq $null) {
			# exe
			$IsMatching = Test-SubstituteExe $LinkPath $ExePath -SetWorkingDirectory:$SetWorkingDirectory
			if ($IsMatching -and !$UseSymlink) {
				Write-Verbose "Command ${CmdName} is already registered for this package."
				return
			}
		} else {
			# symlink
			if ($Item.Target -eq $ExePath -and $UseSymlink) {
				Write-Verbose "Command ${CmdName} is already registered for this package."
				return
			}
		}
	}

	$MatchingCommands = ls $script:BIN_DIR -File -Filter ($CmdName + ".*")

	# there should not be more than 1, if we've done this checking correctly
	if (@($MatchingCommands).Count -gt 1) {
		Write-Warning "Pog developers fucked something up, and there are multiple colliding commands. Plz send bug report."
	}

	# -gt 0 in case the previous if falls through
	if (@($MatchingCommands).Count -gt 0) {
		# TODO: find which package registered the previous command
		$ShouldContinue = ConfirmOverwrite "Overwrite existing command?" `
			("There's already a command '$CmdName' registered by another package.`n" +`
				"To suppress this prompt next time, pass -AllowOverwrite.")

		if (-not $ShouldContinue) {
			Write-Information "Skipped command '$CmdName' registration, user refused to override existing command."
			return
		}

		Write-Warning "Overwriting existing command '${CmdName}'."
		Remove-Item -Force $MatchingCommands
	}

	if ($UseSymlink) {
		$Ext = [System.IO.Path]::GetExtension($ExePath)
		if ($Ext -in @(".cmd", ".bat")) {
			Write-Warning ("When running a batch file (.cmd/.bat) through a symlink, " +
				"the script will think it is located at the symlink location, not in the real location in the package directory, " +
				"which might break paths to other parts of the package.")
		}

		$null = Set-Symlink $LinkPath $ExePath
		Write-Information "Registered command '$CmdName' using symlink."
	} else {
		Write-SubstituteExe $LinkPath $ExePath -SetWorkingDirectory:$SetWorkingDirectory
		Write-Information "Registered command '$CmdName' using substitute exe."
	}
}


Export function Disable-DisplayScaling {
	param(
			[Parameter(Mandatory)]
		$ExePath
	)

	if (-not (Test-Path -Type Leaf $ExePath)) {
		throw "Cannot disable system display scaling - '${ExePath}' is not a file."
	}

	# converted back to string, as registry works with strings
	$ExePath = [string](Resolve-Path $ExePath)

	$RegPath = $APP_COMPAT_REGISTRY_DIR

	if (-not (Test-Path $RegPath)) {
		$null = New-Item $RegPath
	}

	if ((Get-Item $RegPath).Property.Contains($ExePath)) {
		$OldVal = Get-ItemPropertyValue -Path $RegPath -Name $ExePath
		if (($OldVal -split "\s+").Contains("HIGHDPIAWARE")) {
			Write-Verbose "System display scaling already disabled for '${ExePath}'."
			return
		}
		$null = Set-ItemProperty -Path $RegPath -Name $ExePath -Value ($OldVal + " HIGHDPIAWARE")
	} else {
		$null = New-ItemProperty -Path $RegPath -Name $ExePath -PropertyType String -Value "~ HIGHDPIAWARE"
	}
	Write-Information "Disabled system display scaling for '${ExePath}'."
}
