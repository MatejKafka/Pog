Import-Module $PSScriptRoot"\..\Paths"

$PACKAGE_ROOTS | ls | ? {$_.Name[0] -ne "_"} | % {	
	$ManifestDir = Join-Path $MANIFEST_REPO $_.Name
	if (Test-Path $ManifestDir) {
		$ManifestDir = Resolve-Path $ManifestDir
		Write-Warning "Removing previous content at $ManifestDir - should I continue?"
		pause
		ls $ManifestDir | rm -Recurse
	} else {
		$ManifestDir = New-Item -Type Directory -Path $MANIFEST_REPO -Name $_.Name
	}
	$Path = $_
	$MANIFEST_CLEANUP_PATHS | % {Join-Path $Path $_} | % {
		if (Test-Path $_) {
			cp $_ $ManifestDir -Recurse
		}
	}
}