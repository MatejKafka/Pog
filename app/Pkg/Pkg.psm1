. $PSScriptRoot\header.ps1

Import-Module $PSScriptRoot"\Paths"
Import-Module $PSScriptRoot"\Utils"
Import-Module $PSScriptRoot"\Common"


class PkgPackageName : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return ls $script:PACKAGE_ROOTS -Directory | Select -ExpandProperty Name
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
		$AllowOverwrite
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
		}
		
		Invoke-Container $PackagePath $ManifestPath $Manifest.Install $InternalArgs @{}
		echo "Successfully installed $PackageName."
	}
}

Export function New-PkgManifest {
	[CmdletBinding()]
	Param(
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
		
		$manifest = Get-Content -Raw $PSScriptRoot\resources\manifest_template.psd1
		$withName = $manifest.Replace("<NAME>", $PackageName.Replace('"', '""'))
		return New-Item -Path $ManifestPath -Value $withName
	}
}