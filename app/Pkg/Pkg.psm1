# Requires -Version 7
. $PSScriptRoot\header.ps1

Import-Module $PSScriptRoot"\Paths"
Import-Module $PSScriptRoot"\Utils"
Import-Module $PSScriptRoot"\Common"
Import-Module $PSScriptRoot"\Invoke-Container"


class PkgPackageName : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return ls $script:PACKAGE_ROOTS -Directory | select -ExpandProperty Name
    }
}

class PkgRepoManifest : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return ls $script:MANIFEST_REPO -Directory | select -ExpandProperty Name
    }
}

class PkgRepoManifestGenerator : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return ls $script:MANIFEST_GENERATOR_REPO -Directory | select -ExpandProperty Name
    }
}

class PkgPackageRoot : System.Management.Automation.IValidateSetValuesGenerator {
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
		Join-Path $SYSTEM_START_MENU "Pkg"
	} else {
		Join-Path $USER_START_MENU "Pkg"
	}
	
	echo "Exporting shortcuts to '$TargetDir'."
	
	if (Test-Path $TargetDir) {
		echo "Clearing previous Pkg start menu entries..."
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
	return [PkgRepoManifest]::new().GetValidValues()
}

Export function Get-Package {
	[CmdletBinding()]
	param()
	return [PkgPackageName]::new().GetValidValues()
}


function Write-PkgRootList {
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
	Write-PkgRootList
	return "Added $Resolved as package root."
}

Export function Remove-Root {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([PkgPackageRoot], IgnoreCase = $false)]
			[string]
		$RootDir
	)
	
	$Resolved = Resolve-VirtualPath $RootDir
	
	if ($UNRESOLVED_PACKAGE_ROOTS.Contains($Resolved)) {
		$UNRESOLVED_PACKAGE_ROOTS.Remove($Resolved)
		Write-PkgRootList
		return "Removed unresolved package root $Resolved."
	}
	
	$PACKAGE_ROOTS.Remove($Resolved)
	Write-PkgRootList
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
	$ShouldRemove = switch ($Host.UI.PromptForChoice($Title, $Message, @("&Yes", "&No"), 0)) {0 {$true} 1 {$false}}
	if ($ShouldRemove) {
		$RemovedEntries | rm -Recurse
	}
}


Export function Enable- {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([PkgPackageName])]
			[string]
		$PackageName,
			[Hashtable]
		$PkgParameters = @{},
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
		$Manifest = Import-PkgManifestFile $ManifestPath
		
		$ForwardedParams = ExtractParamsFn $PSBoundParameters
		try {
			$PkgParameters = $PkgParameters + $ForwardedParams
		} catch {
			$CmdName = $MyInvocation.MyCommand.Name
			throw "The same parameter was passed to '${CmdName}' both using '-PkgParameters' and forwarded dynamic parameter. " +`
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
		Invoke-Container Enable $ManifestPath $PackagePath $InternalArgs $PkgParameters
		echo "Successfully enabled $PackageName."
	}
}

Export function Install- {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([PkgPackageName])]
			[string]
		$PackageName,
			[Hashtable]
		$PkgParameters = @{},
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
		$Manifest = Import-PkgManifestFile $ManifestPath

		$ForwardedParams = ExtractParamsFn $PSBoundParameters
		try {
			$PkgParameters = $PkgParameters + $ForwardedParams
		} catch {
			$CmdName = $MyInvocation.MyCommand.Name
			throw "The same parameter was passed to '${CmdName}' both using '-PkgParameters' and forwarded dynamic parameter. " +`
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
			DownloadPriority = if ($LowPriority) {"Low"} else {"Foreground"}
		}
		
		Confirm-Manifest $Manifest
		Invoke-Container Install $ManifestPath $PackagePath $InternalArgs $PkgParameters
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
		$TargetPkgRoot,
			[Hashtable]
		$Manifest
	)
	
	$Title = "Overwrite existing package manifest?"
	$ManifestDescription = if ($null -eq $Manifest) {""}
			else {" (manifest '$($Manifest.Name)', version '$($Manifest.Version)')"}
	$Message = "There is already an imported package with name '$TargetName' " +`
			"in '$TargetPkgRoot'$ManifestDescription. Should we overwrite its manifest?"
	$Options = @("&Yes", "&No")
	switch ($Host.UI.PromptForChoice($Title, $Message, $Options, 0)) {
		0 {return $true} # Yes
		1 {return $false} # No
	}
}

Export function Import- {
	[CmdletBinding(PositionalBinding = $false)]
	Param(
			[Parameter(Mandatory, Position = 0)]
			[ValidateSet([PkgRepoManifest])]
			[string]
		$PackageName,
			# TODO: add autocomplete
			[Parameter(Position = 1)]
			[string]
		$Version = "latest",
			[string]
		$TargetName = $PackageName,
			[ValidateSet([PkgPackageRoot])]
			[string]
		$TargetPkgRoot = $script:PACKAGE_ROOTS[0],
			[switch]
		$AllowOverwrite
	)
	
	if ($Version -eq "latest") {
		# find latest version
		$Version = Get-LatestPackageVersion (Join-Path $script:MANIFEST_REPO $PackageName)
	} elseif (-not (Test-Path (Join-Path $script:MANIFEST_REPO $PackageName $Version))) {
		throw "Unknown version of package ${PackageName}: $Version"
	}
	
	$SrcPath = Join-Path $script:MANIFEST_REPO $PackageName $Version
	$TargetPath = Join-Path $TargetPkgRoot $TargetName
	
	if (Test-Path $TargetPath) {
		# target directory already exists
		# let's figure out what it contains
		
		$OrigManifestPath = Get-ManifestPath $TargetPath -NoError
		$OrigManifest = if ($null -eq $OrigManifestPath) {
			# it seems that there is no pkg manifest present
			# either a random folder was erronously created, or this is a package, but corrupted
			Write-Warning "A directory with name '$TargetName' already exists in '$TargetPkgRoot', " +`
					"but it doesn't seem to contain a Pkg manifest. " +`
					"All directories in Pkg root should be packages with valid manifest."
			$null
		} else {
			try {
				Import-PkgManifestFile $OrigManifestPath
			} catch {
				# package has a manifest, but it's invalid (probably corrupted)
				Write-Warning "Found an existing manifest in '$TargetName' at '$TargetPkgRoot', but it's syntactically invalid."
				$null
			}
		}
	
		if (-not $AllowOverwrite -and -not (ConfirmManifestOverwrite $TargetName $TargetPkgRoot $OrigManifest)) {
			throw "There is already a package with name '$TargetName' in '$TargetPkgRoot'. Pass -AllowOverwrite to overwrite current manifest without confirmation."
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
			[ValidateSet([PkgRepoManifest])]
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
			[ValidateSet([PkgPackageName])]
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
			[Parameter(Mandatory)]
			[ValidateSet([PkgRepoManifestGenerator])]
			[string]
		$PackageName,
			[string]
		$Version,
			<# Recreates even existing manifests. #>
			[switch]
		$Force
	)
	
	$GenDir = Join-Path $script:MANIFEST_GENERATOR_REPO $PackageName
	$ManifestDir = Join-Path $script:MANIFEST_REPO $PackageName
	
	if (-not (Test-Path $ManifestDir)) {
		$null = mkdir $ManifestDir
	}
	
	try {
		$GeneratedVersions = if (-not [string]::IsNullOrEmpty($Version)) {
			$Version
		} else {
			# only generate manifests for versions that don't already exist, unless -Force is passed
			$ExistingVersions = ls -Directory $ManifestDir | % {$_.Name}
			# TODO: sort the versions from latest to oldest
			& "$GenDir\versions.ps1" | ? {$Force -or $_ -notin $ExistingVersions} | % {
				if ([string]::IsNullOrEmpty($_)) {
					throw "Empty package version generated by the version generator for package '$PackageName' (either `$null or empty string)"
				}
				echo $_
			}
		}
		
		$GeneratedVersions | % {
			$TargetDir = Join-Path $ManifestDir $_
			if (-not (Test-Path $TargetDir)) {$null = mkdir $TargetDir}
			try {
				echo "Generating manifest '$PackageName', version '$_'..."
				$null = & "$GenDir\generator.ps1" $_ $TargetDir
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

Export function Confirm-RepositoryPackage {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory, ValueFromPipeline)]
			[ValidateSet([PkgRepoManifest])]
			[string]
		$PackageName
	)
	
	process {
		Write-Verbose "Validating package '$PackageName' from local repository..."
		
		$DirPath = Join-Path $script:MANIFEST_REPO $PackageName
		
		$Files = ls $DirPath -File
		if ($Files) {
			Write-Warning "Package '$PackageName' has incorrect structure; root contains following files (only version directories should be present): $Files"
			return
		}
		
		ls $DirPath | % {
			$Version = $_.Name
			
			if (@(ls $_).Count -gt 1) {
				Write-Warning ("In the root of each package manifest directory should be either a single 'manifest.psd1' file, " `
						+ "or a '.manifest' directory containing a 'manifest.psd1' file and other support files or directories. " `
						+ "Instead, multiple files or directories were found for version '$Version' of package '$PackageName'.")
			}
			
			try {
				$ManifestPath = Get-ManifestPath $_
			} catch {
				Write-Warning "Could not find manifest for version '$Version' of package '$PackageName': $_"
				return
			}
			
			try {
				$Manifest = Import-PkgManifestFile $ManifestPath
			} catch {
				Write-Warning $_
				return
			}
			
			try {
				Confirm-Manifest $Manifest $PackageName $Version
			} catch {
				Write-Warning "Validation of package manifest '$PackageName', version '$Version' from local repository failed: $_"
			}
		}
	}
}

# TODO: expand to really check whole package, not just manifest
Export function Confirm-Package {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory, ValueFromPipeline)]
			[ValidateSet([PkgPackageName])]
			[string]
		$PackageName
	)
	
	process {
		$PackagePath = Get-PackagePath $PackageName
		$ManifestPath = Get-ManifestPath $PackagePath
		
		try {
			$Manifest = Import-PkgManifestFile $ManifestPath
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