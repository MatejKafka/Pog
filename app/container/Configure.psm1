. $PSScriptRoot\..\header.ps1

# TODO: implement some form of App Path registration (at least for file and URL association)
#  https://docs.microsoft.com/en-us/windows/win32/shell/app-registration


Import-Module $PSScriptRoot"\Env"
Import-Module $PSScriptRoot"\..\Paths"
Import-Module $PSScriptRoot"\..\Utils"
Import-Module $PSScriptRoot"\..\Common"
Import-Module $PSScriptRoot"\..\command_generator\SubstituteExe.psm1"

Export-ModuleMember -Function Add-SystemEnvVar, Add-SystemEnvPath, Set-SystemEnvVar, Assert-Admin


function Set-Symlink {
	param(
			[Parameter(Mandatory)]
		$LinkPath,
			[Parameter(Mandatory)]
		$TargetPath,
			[switch]
		$RelativeTarget
	)

	if ($RelativeTarget) {
		$Target = $TargetPath
	} elseif ([System.IO.Path]::IsPathRooted($LinkPath) -or [System.IO.Path]::IsPathRooted($TargetPath)) {
		$Target = Resolve-Path $TargetPath
	} else {
		# get relative path for symlink
		$Target = Get-RelativePath (Split-Path $LinkPath) $TargetPath
	}
	
	if (Test-Path $LinkPath) {
		$Item = Get-Item $LinkPath
		if ($Item.Target -eq $Target) {
			return $null # we already have a correct symlink
		}
		# not a correct item, delete and recreate
		if ($Item.PSIsContainer) {
			$Item.Delete($true) # true = delete dir content
		} else {
			$Item.Delete()
		}
	}
	
	#                              \/ using $TargetPath, as $Target may be relative to link
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

	$null = if (-not (Test-Path $TargetPath)) {
		if (-not (Test-Path $OriginalPath)) {
			New-Item -ItemType (if ($Directory) {"Directory"} else {"File"}) $TargetPath
		} else {
			Move-Item $OriginalPath $TargetPath
		}
	}
	
	$result = Set-Symlink $OriginalPath $TargetPath
	if ($null -eq $result) {
		echo "Symlink already exists and matches requested target: '${OriginalPath}'."
	} else {
		echo "Created symlink from '${OriginalPath}' to '${TargetPath}'."
	}
}


<# Ensures that given directory path exists. #>
Export function Assert-Directory {
	param([Parameter(Mandatory)]$Path)

	if (Test-Path -Type Container $Path) {
		echo "Directory '${Path}' already exists."
		return
	}
	if (Test-Path $Path) {
		throw "Path '${Path}' already exists, but it's not a directory."
	}
	$null = New-Item -ItemType Directory $Path
	echo "Created directory '${Path}'."
}


<# Ensures that given file exists. #>
Export function Assert-File {
	param(
			[Parameter(Mandatory)]
			[string]
		$Path,
			[ScriptBlock]
		$DefaultContent = {}
	)

	if (Test-Path -Type Leaf $Path) {
		echo "File '${Path}' already exists."
		return
	}
	if (Test-Path $Path) {
		throw "Path '${Path}' already exists, but it's not a file."
	}
	
	$Parent = Split-Path -Parent $Path
	if (-not(Test-Path $Parent)) {
		New-Item -ItemType Directory $Parent
	}

	# create new file with default content
	& $DefaultContent > $Path
	
	echo "Created file '${Path}'."
}


Export function Export-Shortcut {
	param(
			[Parameter(Mandatory)]
			[string]
		$ShortcutName,
			[Parameter(Mandatory)]
			[string]
		$TargetPath,
		$Arguments,
		$WorkingDirectory,
			[switch]
		$StartMaximized,
		$IconPath
	)
	
	$Arguments = $Arguments -join " "
	
	$Shell = New-Object -ComObject "WScript.Shell"
	# Shell object has different CWD, have to resolve all paths
	$ShortcutPath = Resolve-VirtualPath "./$ShortcutName.lnk"

	if ($TargetPath.Contains("/") -or $TargetPath.Contains("\")) {
		# assume the target is a path
		$Target = Resolve-Path $TargetPath
		
		if ($WorkingDirectory -eq $null) {
			$WorkingDirectory = Split-Path $Target 
		}
		
		if ($IconPath -eq $null) {
			$IconPath = $Target
		}
	} else {
		# assume the target is a command in env:PATH
		$Cmd = Get-Command -CommandType Application $TargetPath -ErrorAction SilentlyContinue
		if ($null -eq $Cmd) {
			throw "Cannot create shortcut to command '$TargetPath', as no such command exists in PATH."
		}
		$Target = $TargetPath
		if ($IconPath -eq $null) {
			$IconPath = $Cmd.Source
		}
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
		echo "Shortcut '$ShortcutName' is already configured."
		return
	}
	
	if (Test-Path $ShortcutPath) {
		echo "Shortcut at '$ShortcutPath' already exists, reusing it..."
	}
	
	$S.TargetPath = $Target
	$S.Arguments = $Arguments
	$S.WorkingDirectory = $WorkingDirectory
	$S.WindowStyle = $WinStyle
	$S.IconLocation = $Icon
	$S.Description = $Description
	
	$S.Save()
	echo "Setup a shortcut called '$ShortcutName' (target: '$TargetPath')."
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
			return "System display scaling already disabled for '${ExePath}'."
		}
		$null = Set-ItemProperty -Path $RegPath -Name $ExePath -Value ($OldVal + " HIGHDPIAWARE")
	} else {
		$null = New-ItemProperty -Path $RegPath -Name $ExePath -PropertyType String -Value "~ HIGHDPIAWARE"
	}
	echo "Disabled system display scaling for '${ExePath}'."
}


Export function Assert-Dependency {
	$Unsatisfied = @()
	$Args | % {
		if ($null -eq (Get-PackagePath -NoError $_)) {
			$Unsatisfied += $_
		} else {
			echo "Validated dependency: ${_}."
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
	
	# ensure BIN_DIR is in env:PATH
	$null = Add-SystemEnvPath -Prepend $script:BIN_DIR
	
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
				return "Command ${CmdName} is already registered for this package."
			}
		} else {
			# symlink
			if ($Item.Target -eq $ExePath -and $UseSymlink) {
				return "Command ${CmdName} is already registered for this package."
			}
		}
	
		if ($global:Pkg_AllowClobber) {
			Write-Warning "Overwriting existing command: '${CmdName}'."
			Remove-Item -Force $LinkPath
		} else {
			throw "Cannot register command '$($CmdName + $LinkExt)', there is already " + `
					"a command under that name. Pass -AllowClobber to overwrite it."
		}
	}
	
	Get-ChildItem $script:BIN_DIR -File `
		| ? {$_.Name -eq $CmdName -or $_.Name -like ($CmdName + ".*")} `
		| % {
			Write-Warning ("There's already another registered command " `
					+ "with the same basename: '$($_.Name)'. Depending on system env:PATHEXT, " `
					+ "the existing command may override the new command.")
		}
	
	if ($UseSymlink) {
		$null = Set-Symlink $LinkPath $ExePath
		echo "Registered command '$CmdName' as symlink."
	} else {
		Write-SubstituteExe $LinkPath $ExePath -SetWorkingDirectory:$SetWorkingDirectory
		echo "Registered command '$CmdName' as substitute exe."
	}
}
