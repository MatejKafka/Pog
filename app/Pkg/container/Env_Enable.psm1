. $PSScriptRoot\..\header.ps1

# TODO: implement some form of App Path registration (at least for file and URL association)
#  https://docs.microsoft.com/en-us/windows/win32/shell/app-registration

Import-Module $PSScriptRoot\Environment
Import-Module $PSScriptRoot\command_generator\SubstituteExe
Import-Module $PSScriptRoot\..\Paths
Import-Module $PSScriptRoot\..\Utils
Import-Module $PSScriptRoot\..\Common
Import-Module $PSScriptRoot\Confirmations

# not sure if we should expose this, as packages really shouldn't need to use admin privilege
# currently, this is used by Notepad++ to optionally redirect Notepad to Notepad++ in Registry
Export-ModuleMember -Function Assert-Admin


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
			# This path must be either non-existent, or already a correct symlink.
			[Parameter(Mandatory)]
		$LinkPath,
			[Parameter(Mandatory)]
		$TargetPath,
			[switch]
		$RelativeTarget
	)

	if ($RelativeTarget) {
		# target is a relative path from $LinkPath
		$Target = $TargetPath
	} elseif ([System.IO.Path]::IsPathRooted($LinkPath) -or [System.IO.Path]::IsPathRooted($TargetPath)) {
		# one of the paths is rooted, use absolute path for symlink
		$Target = Resolve-Path $TargetPath
	} else {
		# get relative path from $LinkPath to $TargetPath for symlink
		$Target = Get-RelativePath (Split-Path $LinkPath) $TargetPath
	}
	
	$LinkPath = Resolve-VirtualPath $LinkPath
	if (Test-Path $LinkPath) {
		$Item = Get-Item $LinkPath
		if ($Item.Target -eq $Target) {
			return $null # we already have a correct symlink
		}
		
		# not a correct item, delete and recreate
		Remove-Item -Recurse $Item
	} else {
		Assert-ParentDirectory $LinkPath
	}
	
	if (Test-Path -Type Container $TargetPath) {
		# it seems New-Item cannot create relative link to directory
		$null = cmd /C mklink /D `"$LinkPath`" `"$Target`"
		return (Get-Item $LinkPath)
	} else {
		New-Item -ItemType SymbolicLink -Path $LinkPath -Target $Target
	}
}

Export function Set-SymlinkedPath {
	param(
			[Parameter(Mandatory)]
		$OriginalPath,
			[Parameter(Mandatory)]
		$TargetPath,
			[switch]
			[Alias("IsDirectory")]
		$Directory
	)

	dynamicparam {
		if ($Directory) {
			return New-DynamicSwitchParam "Merge"
		}
	}

	begin {	
		$ShouldMerge = if ($PSBoundParameters.Keys -contains "Merge") {$PSBoundParameters.Merge} else {$false}

		if ((Test-Path $TargetPath) -and !(Test-Path -Type $(if ($Directory) {"Container"} else {"Leaf"}) $TargetPath)) {
			# mismatch between requested and real target type
			rm -Recurse $TargetPath
		}

		if (-not (Test-Path $TargetPath)) {
			Assert-ParentDirectory $TargetPath
			if ((Test-Path $OriginalPath) -and (Get-Item $OriginalPath).Target -eq $null) {
				# TODO: check if $OriginalPath is being used by another process; block if it is so
				# if source exists and it's not a symlink, move it
				Move-Item $OriginalPath $TargetPath
			} else {
				$null = New-Item $TargetPath -ItemType $(if ($Directory) {"Directory"} else {"File"})
			}
		} elseif ($Directory -and $ShouldMerge -and (Test-Path -PathType Container $OriginalPath) `
				-and $null -eq (Get-Item $OriginalPath).LinkType) {
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
		$DefaultContent = {},
			# if file does exist and this is passed, the script block is ran with reference to the file
			# NOTE: you have to save the output yourself (this was deemed more
			#  robust and often more efficient solution than just returning the desired new content)
			# return $true if something was changed, $false if original content was kept
			[ValidateScript({
				if ($_.GetType() -eq [scriptblock]) {return $true}
				if ($_.GetType() -ne [string]) {
					throw "-ContentUpdater must be either script block or path to PowerShell script file."
				}
				if (Test-Path -Type Leaf $_) {return $true}
				throw "-ContentUpdater is a path string, but it doesn't point to an existing PowerShell script file."
			})]
		$ContentUpdater = $null
	)

	if (Test-Path -Type Leaf $Path) {
		if ($null -eq $ContentUpdater) {
			Write-Verbose "File '$Path' already exists."
			return
		}

		# TODO: this is quite error-prone for the manifest writer, think about ways how to improve it
		$Output = & $ContentUpdater (Get-Item $Path)
		if (@($Output).Count -gt 1) {
			Write-Warning ("ContentUpdater script for Assert-File returned multiple " +`
				"values - check that you [void] output of method calls and commands.")
			$Output = $Output[$Output.Count - 1]
		} elseif (@($Output).Count -eq 0) {
			Write-Warning ("ContentUpdater script for Assert-File did not " +`
				"return any value - it should return `$true or `$false.")
			$Output = $true
		}
		
		if ($Output) {
			Write-Information "File '$Path' updated."
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
	& $DefaultContent > $Path
	
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
		$IconPath
	)
	
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
			throw "Cannot create shortcut to command '$TargetPath', as no such command exists in PATH."
		}
		$Cmd.Source
	}
	
	if ($WorkingDirectory -eq $null) {
		$WorkingDirectory = Split-Path $Target 
	} else {
		$WorkingDirectory = [string](Resolve-Path $WorkingDirectory)
	}

	if ($IconPath -eq $null) {
		$IconPath = $Target
	}
	$IconPath = Resolve-Path $IconPath
	
	# support taking icon from another .lnk
	if (".lnk" -eq (Split-Path -Extension $IconPath)) {
		$Icon = $Shell.CreateShortcut($IconPath).IconLocation
	} else {
		$Icon = [string]$IconPath + ",0"
	}
	
	$WinStyle = if ($StartMaximized) {3} else {1}
	$Description = Split-Path -LeafBase $TargetPath

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
	Write-Information "Setup a shortcut called '$ShortcutName' (target: '$TargetPath')."
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


Export function Assert-Dependency {
	$Unsatisfied = @()
	$Args | % {
		if ($null -eq (Get-PackagePath -NoError $_)) {
			$Unsatisfied += $_
		} else {
			Write-Information "Validated dependency: ${_}."
		}
	}
	
	if ($Unsatisfied.Count -eq 0) {return}
	
	$CallerPackage = if ($MyInvocation.ScriptName -eq "") {
		"<unknown>"
	} else {
		# get parent directory name
		Split-Path -Leaf (Split-Path $MyInvocation.ScriptName)
	}
	
	throw "Unsatisfied dependencies for package ${CallerPackage}: " + ($Unsatisfied -join ", ")
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
			$Matches = Test-SubstituteExe $LinkPath $ExePath -SetWorkingDirectory:$SetWorkingDirectory
			if ($Matches -and !$UseSymlink) {
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

	if (@($MatchingCommands).Count -gt 0) {
		# TODO: find which package registered the previous command
		$ShouldContinue = ConfirmOverwrite "Overwrite existing command?" `
			("There's already a command '$CmdName' registered by another package.`n" +`
				"To suppress this prompt next time, pass -AllowOverwrite.") `
			("Cannot register command '$($CmdName + $LinkExt)', there is already " + `
				"a command under that name. Pass -AllowOverwrite to overwrite it.")

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
		Write-Information "Registered command '$CmdName' as symlink."
	} else {
		Write-SubstituteExe $LinkPath $ExePath -SetWorkingDirectory:$SetWorkingDirectory
		Write-Information "Registered command '$CmdName' as substitute exe."
	}
}
