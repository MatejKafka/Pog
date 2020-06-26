. $PSScriptRoot\header.ps1

Import-Module $PSScriptRoot"\Paths"
Import-Module $PSScriptRoot"\Utils"


Export function Get-PackagePath {
	param(
		[Parameter(Mandatory)]$PackageName,
		[switch]$NoError
	)

	$SearchedPaths = @()
	foreach ($Root in $PACKAGE_ROOTS) {
		$PackagePath = Resolve-VirtualPath (Join-Path $Root $PackageName)
		$SearchedPaths += $PackagePath
		if (Test-Path -Type Container $PackagePath) {
			return $PackagePath
		}
	}
	
	if ($NoError) {
		return $null
	}
	throw ("Could not find package $PackageName in known package directories. " `
			+ "Searched paths:`n" + [String]::Join("`n", $SearchedPaths))
}

Export function Get-ManifestPath {
	param(
		[Parameter(Mandatory)]$PackagePath,
		[switch]$NoError
	)

	$SearchedPaths = @()
	foreach ($ManifestRelPath in $MANIFEST_PATHS) {
		$ManifestPath = Resolve-VirtualPath (Join-Path $PackagePath $ManifestRelPath)
		$SearchedPaths += $ManifestPath
		if (Test-Path -Type Leaf $ManifestPath) {
			return $ManifestPath
		}
	}
	
	if ($NoError) {
		return $null
	}
	$PackageName = Split-Path $PackagePath
	throw ("Could not find manifest file for package $PackageName. " `
			+ "Searched paths:`n" + [String]::Join("`n", $SearchedPaths))
}
