. $PSScriptRoot\header.ps1

Import-Module $PSScriptRoot"\Paths"
Import-Module $PSScriptRoot"\Utils"
Import-Module $PSScriptRoot"\Common"
Import-Module $PSScriptRoot"\Invoke-Container"


class PkgPackageName : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return ls $script:PACKAGE_ROOTS -Directory | Select -ExpandProperty Name
    }
}

class PkgRepoManifest : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return ls $script:MANIFEST_REPO -Directory | Select -ExpandProperty Name
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
	
	ls -File $AppPath | where {
		[IO.Path]::GetExtension($_.Name) -eq ".lnk"
	} | % {
		Copy-Item $_ -Destination $ExportPath
		echo "Exported shortcut $($_.Name) from $(Split-Path -Leaf $AppPath)."
	}
}


Export function Export-PkgShortcutsToStartMenu {
	[CmdletBinding()]
	param(
			[switch]
		$UseSystemWideMenu,
			[switch]
		$ExcludeUnderscoredPackages
	)
	
	$TargetDir = if ($UseSystemWideMenu) {
		Join-Path $SYSTEM_START_MENU $PKG_NAME
	} else {
		Join-Path $USER_START_MENU $PKG_NAME
	}
	
	echo "Exporting shortcuts to $TargetDir."
	
	if (Test-Path $TargetDir) {
		echo "Clearing previous $PKG_NAME start menu entries..."
		Remove-Item -Recurse $TargetDir
	}
	$null = New-Item -ItemType Directory $TargetDir
	
	ls $PACKAGE_ROOTS -Directory | where {
		-not ($ExcludeUnderscoredPackages -and $_.Name.StartsWith("_"))
	} | % {
		Export-AppShortcuts $_.FullName $TargetDir
	}
}


Export function Get-PkgPackage {
	[CmdletBinding()]
	param()	
	return [PkgRepoManifest]::new().GetValidValues()
}

Export function Get-PkgInstalledPackage {
	[CmdletBinding()]
	param()
	return [PkgPackageName]::new().GetValidValues()
}


function Write-PkgRootList {
	($PACKAGE_ROOTS + $UNRESOLVED_PACKAGE_ROOTS) | Set-Content $PACKAGE_ROOT_FILE
}

Export function Get-PkgRoot {
	return $PACKAGE_ROOTS + $UNRESOLVED_PACKAGE_ROOTS
}

Export function New-PkgRoot {
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

Export function Remove-PkgRoot {
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


Export function Enable-Pkg {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([PkgPackageName])]
			[string]
		$PackageName,
			[Hashtable]
		$PkgParams = @{},
			# allows overriding existing commands without confirmation
			[switch]
		$AllowOverwrite
	)

	#dynamicparam {
		# create dynamic parameters based on manifest parameters for matching package
		# see https://github.com/PowerShell/PowerShell/issues/6585 for a way to do this
	#}
	
	begin {
		$PackagePath = Get-PackagePath $PackageName		
		$ManifestPath = Get-ManifestPath $PackagePath
		$Manifest = Import-PowerShellDataFile $ManifestPath
		
		if ($Manifest.ContainsKey("Private") -and $Manifest.Private) {
			echo "Enabling private package $PackageName..."
		} elseif ($Manifest.Name -eq $PackageName) {
			echo "Enabling package $($Manifest.Name), version $($Manifest.Version)..."
		} else {
			echo "Enabling package $($Manifest.Name) (installed as $PackageName), version $($Manifest.Version)..."
		}
		
		$InternalArgs = @{
			Manifest = $Manifest
			AllowOverwrite = [bool]$AllowOverwrite
		}
		
		Invoke-Container $PackagePath $ManifestPath Enable $Manifest.Enable $InternalArgs $PkgParams -Verbose:$VerbosePreference
		echo "Successfully enabled $PackageName."
	}
}

Export function Install-Pkg {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateSet([PkgPackageName])]
			[string]
		$PackageName,
			# allow overwriting current .\app directory, if one exists
			[switch]
		$AllowOverwrite,
			# download files with low priority, which results in better network
			#  responsiveness for other programs, but possibly slower download
			[switch]
		$LowPriority
	)
	
	begin {
		$PackagePath = Get-PackagePath $PackageName		
		$ManifestPath = Get-ManifestPath $PackagePath		
		$Manifest = Import-PowerShellDataFile $ManifestPath
		
		if ($Manifest.Name -eq $PackageName) {
			echo "Installing package $($Manifest.Name), version $($Manifest.Version)..."
		} else {
			echo "Installing package $($Manifest.Name) (installed as $PackageName), version $($Manifest.Version)..."
		}
		
		$InternalArgs = @{
			Manifest = $Manifest
			AllowOverwrite = [bool]$AllowOverwrite
			DownloadPriority = if ($LowPriority) {"Low"} else {"Foreground"}
		}
		
		Invoke-Container $PackagePath $ManifestPath Install $Manifest.Install $InternalArgs @{} -Verbose:$VerbosePreference
		echo "Successfully installed $PackageName."
	}
}

Export function Import-Pkg {
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
		if (!$AllowOverwrite) {
			throw "There is already an initialized package with name '$TargetName' in '$TargetPkgRoot'. Pass -AllowOverwrite to overwrite current manifest."
		}
		echo "Overwriting previous package manifest..."
		$script:MANIFEST_CLEANUP_PATHS | % {Join-Path $TargetPath $_} | ? {Test-Path $_} | rm -Recurse
	} else {
		$null = New-Item -Type Directory $TargetPath
	}
	
	ls $SrcPath | Copy-Item -Destination $TargetPath -Recurse
	echo "Initialized '$TargetPath' with package manifest '$PackageName' (version $Version)."
}

Export function Get-PkgManifest {
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

Export function New-PkgManifest {
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
	
		if ($PackageName -notin [PkgRepoManifest]::new().GetValidValues()) {
			# manifest directory for this package does not exist
			New-Item -Type Directory $PackagePath
		}
		
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

Export function New-PkgDirectManifest {
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

function Validate-Manifest {
	param(
			[Parameter(Mandatory)]
			[Hashtable]
		$Manifest,
			[string]
		$ExpectedName,
			[string]
		$ExpectedVersion
	)
	
	if ("Private" -in $Manifest.Keys -and $Manifest.Private) {
		Write-Verbose "Skipped validation of private package manifest '$Manifest.Name'."
		return
	}
	
	$RequiredKeys = @{
		"Name" = [string]; "Version" = [string]; "Architecture" = @([string], [Object[]]);
		"Enable" = [scriptblock]; "Install" = [scriptblock]
	}
	
	$OptionalKeys = @{
		"Description" = [string]
	}
	
	
	$Issues = @()
	
	$RequiredKeys.GetEnumerator() | % {
		$StrTypes = $_.Value -join " | "
		if (!$Manifest.ContainsKey($_.Key)) {
			$Issues += "Missing manifest property '$($_.Key)' of type '$StrTypes'."
			return
		}
		$RealType = $Manifest[$_.Key].GetType()
		if ($RealType -notin $_.Value) {
			$Issues += "Property '$($_.Key)' is present, but has incorrect type '$RealType', expected '$StrTypes'."
		}
	}
	
	$OptionalKeys.GetEnumerator() | ? {$Manifest.ContainsKey($_.Key)} | % {
		$RealType = $Manifest[$_.Key].GetType()
		if ($RealType -notin $_.Value) {
			$StrTypes = $_.Value -join " | "
			$Issues += "Optional property '$($_.Key)' is present, but has incorrect type '$RealType', expected '$StrTypes'."
		}
	}
	
	$AllowedKeys = $RequiredKeys.Keys + $OptionalKeys.Keys
	$Manifest.Keys | ? {-not $_.StartsWith("_")} | ? {$_ -notin $AllowedKeys} | % {
		$Issues += "Found unknown property '$_' - private properties must be prefixed with underscore ('_PrivateProperty')."
	}
	
	
	if ($Manifest.ContainsKey("Name")) {
		if (-not [string]::IsNullOrEmpty($ExpectedName) -and $Manifest.Name -ne $ExpectedName) {
			$Issues += "Incorrect 'Name' property value - got '$($Manifest.Name)', expected '$ExpectedName'."
		}
	}
	
	if ($Manifest.ContainsKey("Version")) {
		if (-not [string]::IsNullOrEmpty($ExpectedVersion) -and $Manifest.Version -ne $ExpectedVersion) {
			$Issues += "Incorrect 'Version' property value - got '$($Manifest.Version)', expected '$ExpectedVersion'."
		}	
	}
	
	if ($Manifest.ContainsKey("Architecture")) {
		$ValidArch = @("x64", "x86", "*")
		if (@($Manifest.Architecture | ? {$_ -notin $ValidArch}).Count -gt 0) {
			$Issues += "Invalid 'Architecture' value - got '$($Manifest.Architecture)', expected one of $ValidArch, or an array."
		}
	}
	
	if ($Issues.Count -gt 1) {
		throw ("Multiple issues encountered when validating manifest:`n`t" + ($Issues -join "`n`t"))
	} elseif ($Issues.Count -eq 1) {
		throw $Issues
	}
	
	Write-Verbose "Manifest is valid."
}

Export function Confirm-PkgPackage {
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
			Write-Warning "Package '$PackageName' has incorrect structure; root contains following files (only directories should be present): $Files"
			return
		}
		
		ls $DirPath | % {
			$Version = $_.Name
			try {
				$ManifestPath = Get-ManifestPath $_
			} catch {
				Write-Warning "Could not find manifest for version '$Version' of '$PackageName': $_"
				return
			}
			
			$Manifest = Import-PowerShellDataFile $ManifestPath
			try {
				Validate-Manifest $Manifest $PackageName $Version
			} catch {
				Write-Warning "Validation of package manifest '$PackageName', version '$Version' from local repository failed: $_"
			}			
		}
	}
}

Export function Confirm-PkgImportedManifest {
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
		$Manifest = Import-PowerShellDataFile $ManifestPath
		
		Write-Verbose "Validating imported package manifest '$PackageName' at '$ManifestPath'..."
		try {
			Validate-Manifest $Manifest
		} catch {
			Write-Warning "Validation of imported package manifest '$PackageName' at '$ManifestPath' failed: $_"
		}
	}
}