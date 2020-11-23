Import-Module $PSScriptRoot"\..\Paths"

ls -Directory $MANIFEST_REPO | % {
	$Name = $_.Name
	ls $_ -Recurse -Include manifest.psd1 `
		| Import-PowerShellDataFile `
		| ? {"Private" -in $_.Keys -and $_.Private} `
		| % {
			echo $Name
		}
}