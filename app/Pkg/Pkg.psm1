. $PSScriptRoot\header.ps1

Import-Module $PSScriptRoot"\Paths"
Import-Module $PSScriptRoot"\Utils"
Import-Module $PSScriptRoot"\Common"


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


function New-ScriptPosition {
	param($SrcFile, $LineNum, $ColumnNum, $Line)
	return [System.Management.Automation.Language.ScriptPosition]::new(
			$SrcFile, $LineNum, $ColumnNum, $Line, $null)
}

<# 
 Adds src script info to reconstructed error record.
 #>
function Set-ErrorSrcFile {
	param($Err, $SrcFile)

	$Src = $SrcFile + ":Enable"
	$Line = $Err.InvocationInfo.Line
	$Field = [System.Management.Automation.InvocationInfo].GetField("_scriptPosition", "static,nonpublic,instance")
	$Extent = $Field.GetValue($Err.InvocationInfo)

	$Err.InvocationInfo.DisplayScriptPosition = [System.Management.Automation.Language.ScriptExtent]::new(
		(New-ScriptPosition $Src $Extent.StartLineNumber $Extent.StartColumnNumber $Line),
		(New-ScriptPosition $Src $Extent.EndLineNumber $Extent.EndColumnNumber $Line)
	) 
	
}

function Invoke-Container {
	param(
			[Parameter(Mandatory)]
			[string]
		$WorkingDirectory,
			[Parameter(Mandatory)]
			[string]
		$ScriptFile,
			[Parameter(Mandatory)]
			[ScriptBlock]
		$ScriptBlock,
			[Parameter(Mandatory)]
			[Hashtable]
		$InternalArguments,
			[Parameter(Mandatory)]
			[Hashtable]
		$ScriptArguments
	)
	
	$ContainerJob = Start-Job -WorkingDirectory $WorkingDirectory -FilePath $CONTAINER_SCRIPT `
			-InitializationScript ([ScriptBlock]::Create(". $CONTAINER_SETUP_SCRIPT")) `
			-ArgumentList @($ScriptBlock, $InternalArguments, $ScriptArguments)
	
	try {
		# FIXME: this breaks error source
		# FIXME: Original error type is lost (changed to generic "Exception")
		Receive-Job -Wait $ContainerJob
	} finally {
		Stop-Job $ContainerJob
		Remove-Job $ContainerJob
	}
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
			[switch]
		$AllowClobber
	)

	#dynamicparam {
		# create dynamic parameters based on manifest parameters for matching package
		# see https://github.com/PowerShell/PowerShell/issues/6585 for a way to do this
	#}
	
	begin {
		$PackagePath = Get-PackagePath $PackageName		
		$ManifestPath = Get-ManifestPath $PackagePath
		
		$Manifest = Import-PowerShellDataFile $ManifestPath
		if ($Manifest.Name -eq $PackageName) {
			echo "Enabling package $($Manifest.Name), version $($Manifest.Version)..."
		} else {
			echo "Enabling package $($Manifest.Name) (installed as $PackageName), version $($Manifest.Version)..."
		}
		
		$InternalArgs = @{
			Manifest = $Manifest
			AllowClobber = [bool]$AllowClobber
		}
		
		Invoke-Container $PackagePath $ManifestPath $Manifest.Enable $InternalArgs $PkgParams
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
		
		Invoke-Container $PackagePath $ManifestPath $Manifest.Install $InternalArgs @{}
		echo "Successfully installed $PackageName."
	}
}

Export function Import-PkgPackage {
	[CmdletBinding()]
	Param(
			[Parameter(Mandatory)]
			[ValidateSet([PkgRepoManifest])]
			[Alias("PackageName")]
			[string]
		$ManifestName,
			[string]
		$TargetName,
			[ValidateSet([PkgPackageRoot])]
			[string]
		$TargetPkgRoot = $script:PACKAGE_ROOTS[0],
			[switch]
		$AllowOverwrite
	)
	
	if ([string]::IsNullOrEmpty($TargetName)) {
		$TargetName = $ManifestName
	}
	
	$SrcPath = Join-Path $script:MANIFEST_REPO $ManifestName
	$TargetPath = Join-Path $TargetPkgRoot $TargetName
	
	if (Test-Path $TargetPath) {
		if (!$AllowOverwrite) {
			throw "There is already an initialized package with name '$TargetName' in '$TargetPkgRoot'. Pass -AllowOverwrite to overwrite current manifest."
		}
		echo "Overwriting previous package manifest..."
		$script:MANIFEST_CLEANUP_PATHS | % {
			rm -Recurse (Join-Path $TargetPath $_)
		}
	} else {
		$null = New-Item -Type Directory $TargetPath
	}
	
	ls $SrcPath | Copy-Item -Destination $TargetPath -Recurse
	echo "Initialized '$TargetPath' with package manifest '$ManifestName'."
}

Export function New-PkgManifest {
	[CmdletBinding()]
	param(
			[Parameter(Mandatory)]
			[ValidateScript({
				if ($_ -in (new-object PkgRepoManifest).GetValidValues()) {
					throw "Package manifest with name '$_' already exists in local repository."
				}
				return $true
			})]
			[Alias("PackageName")]
			[string]
		$ManifestName
	)

	begin {
		$ManifestDir = New-Item -Type Directory -Path $script:MANIFEST_REPO -Name $ManifestName
		$ManifestPath = Resolve-VirtualPath (Join-Path $ManifestDir $MANIFEST_PATHS[0])
		
		if (Test-Path $ManifestPath) {
			throw "Package $PackageName already has a manifest at '$ManifestPath'."
		}
		
		$manifest = Get-Content -Raw $PSScriptRoot\resources\manifest_template.psd1
		$withName = $manifest.Replace("<NAME>", $ManifestName.Replace('"', '""'))
		return New-Item -Path $ManifestPath -Value $withName
	}
}

Export function Copy-PkgManifestsToRepository {
	[CmdletBinding()]
	param()
	
	# TODO: rewrite, buggy
	
	$script:PACKAGE_ROOTS | ls | ? {$_.Name[0] -ne "_"} | % {	
		$ManifestDir = Join-Path $script:MANIFEST_REPO $_.Name
		if (Test-Path $ManifestDir) {
			$ManifestDir = Resolve-Path $ManifestDir
			ls $ManifestDir | rm -Recurse
		} else {
			$ManifestDir = New-Item -Type Directory -Path $script:MANIFEST_REPO -Name $_.Name
		}
		$Path = $_
		$script:MANIFEST_CLEANUP_PATHS | % {Join-Path $Path $_} | % {
			if (Test-Path $_) {
				cp $_ $ManifestDir -Recurse
			}
		}
	}
}