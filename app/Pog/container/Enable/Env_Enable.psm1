# Requires -Version 7
using module ..\..\Paths.psm1
using module ..\..\lib\Utils.psm1
using module ..\container_lib\Environment.psm1
using module ..\container_lib\Confirmations.psm1
. $PSScriptRoot\..\..\lib\header.ps1


Export-ModuleMember -Function Confirm-Action
# not sure if we should expose this, PowerShell (private package) uses it to set PSModulePath
Export-ModuleMember -Function Add-EnvVar, Set-EnvVar
Export-ModuleMember -Cmdlet Export-Command


function SetupInternalState {
	[CmdletBinding()]
	param()

	# moved to a separate function, because we need [CmdletBinding()] to get $PSCmdlet
	$null = [Pog.ContainerEnableInternalState]::InitCurrent($PSCmdlet, $global:_Pog.Package)
}

<# This function is called after the container setup is finished to run the Enable script. #>
Export function __main {
	# __main must NOT have [CmdletBinding()], otherwise we lose error message position from the manifest scriptblock
	param($Manifest, $PackageArguments)

	SetupInternalState

	# invoke the scriptblock
	# without .GetNewClosure(), the script block would see our internal module functions, probably because
	#  it would be automatically bound to our SessionState; not really sure why GetNewClosure() binds it to
	#  a different scope
	& $Manifest.Enable.GetNewClosure() @PackageArguments
}

<# This function is called after the Enable script finishes. #>
Export function __cleanup {
	[CmdletBinding()]
	param()

	# remove stale shortcuts and commands
	$InternalState = [Pog.ContainerEnableInternalState]::GetCurrent($PSCmdlet)

	if ($InternalState.StaleShortcuts.Count -gt 0 -or $InternalState.StaleShortcutStubs.Count -gt 0) {
		Write-Verbose "Removing stale shortcuts..."
		$InternalState.StaleShortcuts | % {
			Remove-Item $_
			Write-Information "Removed stale shortcut '$_'."
		}
		Remove-Item $InternalState.StaleShortcutStubs
	}

	if ($InternalState.StaleCommands.Count -gt 0) {
		Write-Verbose "Removing stale commands..."
		$InternalState.StaleCommands | % {
			Remove-Item $_
			Write-Information "Removed stale command '$_'."
		}
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
		Write-Debug "Using absolute path as symlink target."
		[string]$Target
	} else {
		# get relative path from $LinkPath to $TargetPath for symlink
		# use parent of $LinkPath, as relative symlinks are resolved from parent dir
		[Pog.FsUtils]::GetRelativePath((Split-Path $LinkAbsPath), $Target)
	}

	if (Test-Path $LinkAbsPath) {
		if ($TargetStr -eq [Pog.FsUtils]::GetSymbolicLinkTarget($LinkAbsPath)) {
			return $null # we already have a correct symlink
		}

		# not a correct item, delete and recreate
		Remove-Item -Recurse $LinkAbsPath
	} else {
		Assert-ParentDirectory $LinkAbsPath
	}

	Write-Debug "Creating symlink from '$LinkAbsPath' with target '$TargetStr'."
	# New-Item -Type SymbolicLink has a dumb issue with relative paths, so we use the .NET methods instead
	#  https://github.com/PowerShell/PowerShell/issues/15235
	[Pog.FsUtils]::CreateSymbolicLink($LinkAbsPath, $TargetStr, $Target.PSIsContainer)
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
		$OriginalPathIsSymlink = (Test-Path $OriginalPath) -and $null -ne (Get-Item $OriginalPath).LinkType

		# if orig exists and doesn't match expected item type
		if ((Test-Path $OriginalPath) -and -not $OriginalPathIsSymlink -and -not (Test-Path -Type $TestType $OriginalPath)) {
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
			if ((Test-Path $OriginalPath) -and -not $OriginalPathIsSymlink) {
				# TODO: check if $OriginalPath is being used by another process; block if it is so
				# move it to target and then create symlink
				Move-Item $OriginalPath $TargetPath
			} else {
				$null = New-Item $TargetPath -ItemType $ItemType
			}
		} elseif ($Merge -and -not $OriginalPathIsSymlink) {
			Write-Information "Merging directory $OriginalPath to $TargetPath..."
			Merge-Directories $OriginalPath $TargetPath
		}

		$result = Set-Symlink $OriginalPath $TargetPath
		if ($null -eq $result) {
			Write-Verbose "Symlink at '$OriginalPath' already exists and matches requested target '$TargetPath'."
		} else {
			Write-Information "Created symlink from '$OriginalPath' to '$TargetPath'."
		}
	}
}


<# Ensures that given directory path exists. #>
Export function Assert-Directory {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory)]$Path,
		[scriptblock]$DefaultContent = {param($Dir)}
	)

	if (Test-Path -Type Container $Path) {
		Write-Verbose "Directory '$Path' already exists."
		return
	}
	if (Test-Path $Path) {
		throw "Path '$Path' already exists, but it's not a directory."
	}
	$Dir = New-Item -ItemType Directory $Path
	try {
		& $DefaultContent $Dir
	} catch {
		Remove-Item -Recurse -Force $Dir
	}
	Write-Information "Created directory '$Path'."
}


<# Ensures that given file exists. #>
Export function Assert-File {
	[CmdletBinding(DefaultParameterSetName="ScriptBlocks")]
	param(
			[Parameter(Mandatory, Position=0)]
			[string]
		$Path,
			# if file does not exist, use output of this script block to populate it
			# file is left empty if this is not passed
			[Parameter(Position=1, ParameterSetName="ScriptBlocks")]
			[ValidateScript({
				if ($_ -is [scriptblock]) {return $true}
				if ($_ -is [string]) {
					if (Test-Path -Type Leaf $_) {return $true}
					throw "-DefaultContent is a template path string, but it doesn't point to an existing file: '${_}'"
				}
				throw "-DefaultContent must be either a script block, or a path to an existing template file, got '$($_.GetType())'."
			})]
		$DefaultContent = {},
			# If file does exist and this is passed, the script block is ran with reference to the file.
			# NOTE: You have to save the output yourself (this was deemed a more robust and typically more efficient
			#       solution than just returning the desired new content).
			[Parameter(Position=2, ParameterSetName="ScriptBlocks")]
			[scriptblock]
		$ContentUpdater = $null,
			[Parameter(ParameterSetName="FixedContent")]
			[ValidateScript({
				if ($_ -is [scriptblock] -or $_ -is [string]) {return $true}
				throw "-FixedContent must be either a script block, or a string, got '$($_.GetType())'."
			})]
		$FixedContent = $null
	)

	$FixedContentStr = if ($FixedContent -is [scriptblock]) {
		Invoke-DollarUnder $FixedContent (Resolve-VirtualPath $Path)
	} elseif ($FixedContent -is [string]) {
		$FixedContent
	} else {$null}

	if (Test-Path -Type Leaf $Path) {
		if ($FixedContentStr) {
			if ((Get-Content -Raw $Path) -eq $FixedContentStr) {
				Write-Verbose "File '$Path' exists with already correct content."
			} else {
				Set-Content -Path $Path -Value $FixedContentStr -NoNewline
				Write-Information "File '$Path' updated."
			}
			return
		}

		if ($null -eq $ContentUpdater) {
			Write-Verbose "File '$Path' already exists."
			return
		}

		# if $Path points to a file symlink, work with the target (otherwise the change detection below would not work)
		$ResolvedPath = Resolve-VirtualPath $Path
		$PathTarget = [Pog.FsUtils]::GetSymbolicLinkTarget($ResolvedPath)
		if ($PathTarget) {
			$ResolvedPath = $PathTarget
		}

		$File = Get-Item $ResolvedPath
		$null = Invoke-DollarUnder $ContentUpdater $File

		$WasChanged = $File.LastWriteTime -ne (Get-Item $ResolvedPath).LastWriteTime
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
	$NewContent = if ($FixedContentStr) {$FixedContentStr}
		elseif ($DefaultContent -is [string]) {Copy-Item $DefaultContent $Path}
		else {Invoke-DollarUnder $DefaultContent (Resolve-VirtualPath $Path)}

	if (-not (Test-Path $Path)) {
		Set-Content $Path $NewContent
	}
	Write-Information "Created file '$Path'."
}


Export function Export-Shortcut {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[string]
		$ShortcutName,
			[Parameter(Mandatory)]
			[string]
		$TargetPath,
			[Alias("Arguments")]
			[string[]]
		$ArgumentList,
		$WorkingDirectory,
			[Alias("Icon")]
		$IconPath,
		$Description,
    		[Alias("Environment")]
    		[Hashtable]
		$EnvironmentVariables
	)

	# shortcut takes a command line string, not an argument array
	$CommandLine = if ($ArgumentList) {[Pog.Native.Win32Args]::EscapeArguments($ArgumentList)} else {""}

	$Shell = New-Object -ComObject "WScript.Shell"
	# Shell object has different CWD, have to resolve all paths
	$ShortcutPath = Resolve-VirtualPath ([Pog.PathConfig+PackagePaths]::ShortcutDirRelPath + "/$ShortcutName.lnk")

	$Target = Resolve-VirtualPath $TargetPath
	if (-not [System.IO.File]::Exists($Target)) {
		throw "Shortcut target does not exist: $Target"
	}

	Write-Debug "Resolved shortcut target: $Target"

	if ($WorkingDirectory -eq $null) {
		$WorkingDirectory = Split-Path $Target
	} else {
		$WorkingDirectory = Resolve-VirtualPath $WorkingDirectory
		if (-not [System.IO.Directory]::Exists($WorkingDirectory)) {
			throw "Shortcut working directory does not exist: $WorkingDirectory"
		}
	}

	if ($IconPath -eq $null) {
		$IconPath = $Target
	} else {
		$IconPath = Resolve-VirtualPath $IconPath
		if (-not [System.IO.File]::Exists($IconPath)) {
			throw "Shortcut icon does not exist: $IconPath"
		}
	}

	# support copying icon from another .lnk
	if (".lnk" -eq [System.IO.Path]::GetExtension($IconPath)) {
		$Icon = $Shell.CreateShortcut($IconPath).IconLocation
	} else {
		# icon index 0 = first icon in the file
		$Icon = [string]$IconPath + ",0"
	}

	if ($null -eq $Description) {
		$Description = [System.IO.Path]::GetFileNameWithoutExtension($TargetPath)
	}


	if ($EnvironmentVariables) {
		Write-Debug "Creating a hidden stub to set environment variables..."
		# if -EnvironmentVariables was used, create a hidden command and point the shortcut to it,
		#  since shortcuts cannot set environment variables
		$Target = Export-Command -_InternalDoNotUse_Shortcut -PassThru `
			$ShortcutName $TargetPath -EnvironmentVariables $EnvironmentVariables `
			-Verbose:$false -Debug:$false -InformationAction SilentlyContinue
	}


	# this shortcut was refreshed, not stale, remove it
	# noop when not present
	$null = [Pog.ContainerEnableInternalState]::GetCurrent($PSCmdlet).StaleShortcuts.Remove($ShortcutPath)

	$S = $Shell.CreateShortcut($ShortcutPath)

	if (Test-Path $ShortcutPath) {
		if ($S.TargetPath -eq $Target `
				-and $S.Arguments -eq $CommandLine `
				-and $S.WorkingDirectory -eq $WorkingDirectory `
				-and $S.IconLocation -eq $Icon `
				-and $S.Description -eq $Description) {
			Write-Verbose "Shortcut '$ShortcutName' is already configured."
			return
		} else {
			Write-Verbose "Shortcut at '$ShortcutPath' already exists, reusing it..."
		}
	}

	$S.TargetPath = $Target
	$S.Arguments = $CommandLine
	$S.WorkingDirectory = $WorkingDirectory
	$S.IconLocation = $Icon
	$S.Description = $Description

	$S.Save()
	Write-Information "Set up a shortcut called '$ShortcutName' (target: '$TargetPath')."
}


Export function Disable-DisplayScaling {
	param(
			[Parameter(Mandatory)]
			[string]
		$ExePath
	)

	$OrigExePath = $ExePath
	$ExePath = Resolve-VirtualPath $ExePath

	if (-not [System.IO.File]::Exists($ExePath)) {
		throw "Cannot disable system display scaling, target does not exist: ${OrigExePath}"
	}

	# display scaling can be disabled using the application manifest of the executable
	$Manifest = [Pog.Native.PeApplicationManifest]::new($ExePath)
	if ($Manifest.EnsureDpiAware()) {
		$Manifest.Save()
		Write-Information "Disabled system display scaling for '${OrigExePath}'."
	} else {
		Write-Verbose "System display scaling already disabled for '${OrigExePath}'."
	}
}