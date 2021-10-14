# Requires -Version 7
. $PSScriptRoot\..\header.ps1

# TODO: implement some form of App Path registration (at least for file and URL association)
#  https://docs.microsoft.com/en-us/windows/win32/shell/app-registration
#  https://docs.microsoft.com/en-us/windows/win32/shell/fa-verbs
#  https://docs.microsoft.com/en-us/windows/win32/shell/fa-how-work
# Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths
# Computer\HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts
# Computer\HKEY_CLASSES_ROOT\Applications

Import-Module $PSScriptRoot\Environment
Import-Module $PSScriptRoot\command_generator\SubstituteExe
Import-Module $PSScriptRoot\..\Paths
Import-Module $PSScriptRoot\..\Utils
Import-Module $PSScriptRoot\Confirmations


Export-ModuleMember -Function Confirm-Action

# not sure if we should expose this, as packages really shouldn't need to use admin privilege
# currently, this is used by Notepad++ to optionally redirect Notepad to Notepad++ in Registry
Export-ModuleMember -Function Assert-Admin

# also not sure about this, PowerShell (private package) uses it to set PSModulePath
Export-ModuleMember -Function Add-EnvVar, Set-EnvVar


enum ItemType {File; Directory}


# set of all shortcuts that were not "refreshed" during this Enable call
# starts with all shortcuts found in package, and each time Export-Shortcut is called, it is removed
# before end of Enable, all shortcuts still in this set are deleted
$StaleShortcuts = New-Object System.Collections.Generic.HashSet[string]
ls -File -Filter "./*.lnk" | % {$StaleShortcuts.Add($_.BaseName)}
Write-Debug "Listed original shortcuts."

<# This function is called after the Enable script finishes. #>
Export function _pkg_cleanup {
	# remove stale shortcuts
	if ($StaleShortcuts.Count -gt 0) {
		Write-Verbose "Removing stale shortcuts..."
	}
	$StaleShortcuts | % {
		rm ("./" + $_ + ".lnk")
		Write-Verbose "Removed stale shortcut '$_'."
	}
}

<# This function is called after the container setup is finished to run the passed script. #>
Export function _pkg_main {
	param($EnableSb, $PkgArguments)

	# invoke the scriptblock
	& $EnableSb @PkgArguments
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
			rm -Recurse $Target
		}
		Move-Item $_ $Target
	}
	rm -Recurse $SrcDir
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

	$LinkAbs = Resolve-VirtualPath $LinkPath
	# this one must exist
	$TargetAbs = Resolve-Path $TargetPath

	[string]$TargetStr = if ([System.IO.Path]::IsPathRooted($LinkPath) -or [System.IO.Path]::IsPathRooted($TargetPath)) {
		# one of the paths is rooted, use absolute path for symlink
		$TargetAbs
	} else {
		# get relative path from $LinkPath to $TargetPath for symlink
		# use parent of $LinkPath, as relative symlinks are resolved from parent dir
		[IO.Path]::GetRelativePath((Split-Path $LinkAbs), $TargetAbs)
	}

	if (Test-Path $LinkAbs) {
		$Item = Get-Item $LinkAbs
		if ($Item.Target -eq $TargetStr) {
			return $null # we already have a correct symlink
		}

		# not a correct item, delete and recreate
		Remove-Item -Recurse $Item
	} else {
		Assert-ParentDirectory $LinkAbs
	}

	Write-Debug "Creating symlink from '$LinkAbs' with target '$TargetStr'."
	# New-Item -Type SymbolicLink has some dumb issues with relative paths - surprisingly, it seems safer to use mklink
	#  see https://github.com/PowerShell/PowerShell/pull/12797#issuecomment-819169817
	$null = if (Test-Path -Type Container $TargetAbs) {
		cmd.exe /C mklink /D $LinkAbs $TargetStr
	} else {
		cmd.exe /C mklink $LinkAbs $TargetStr
	}
	return Get-Item $LinkAbs
}

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
			rm -Recurse $TargetPath
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
			[Alias("Icon")]
		$IconPath,
		$Description
	)

	# this shortcut was refreshed, not stale, remove it
	# noop when not present
	$null = $StaleShortcuts.Remove($ShortcutName)

	# FIXME: this doesn't look correct (wrt whitespace escaping)
	$Arguments = $Arguments -join " "

	$Shell = New-Object -ComObject "WScript.Shell"
	# Shell object has different CWD, have to resolve all paths
	$ShortcutPath = Resolve-VirtualPath "./$ShortcutName.lnk"

	$Target = if ($TargetPath.Contains("/") -or $TargetPath.Contains("\")) {
		# assume the target is a path
		Resolve-Path $TargetPath
	} else {
		# assume the target is a command in env:PATH
		$Cmd = Get-Command -CommandType Application $TargetPath -ErrorAction SilentlyContinue
		if ($null -eq $Cmd) {
			throw "Cannot create shortcut to command '$TargetPath', as no such command is known by the system (present in env:PATH)."
		}
		Resolve-Path $Cmd.Source
	}

	Write-Debug "Resolved shortcut target: $Target"

	if ($WorkingDirectory -eq $null) {
		$WorkingDirectory = Split-Path $Target
	} else {
		$WorkingDirectory = Resolve-Path $WorkingDirectory
	}

	if ($IconPath -eq $null) {
		$IconPath = $Target
	}
	$IconPath = Resolve-Path $IconPath

	# support copying icon from another .lnk
	if (".lnk" -eq (Split-Path -Extension $IconPath)) {
		$Icon = $Shell.CreateShortcut($IconPath).IconLocation
	} else {
		$Icon = [string]$IconPath + ",0"
	}

	$WinStyle = if ($StartMaximized) {3} else {1}
	
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

	# TODO: check if pkg_bin is in PATH, and warn the user if it's not
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
		Write-Warning "Pkg developers fucked something up, and there are multiple colliding commands. Plz send bug report."
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
