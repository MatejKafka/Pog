Import-Module $PSScriptRoot"\..\Paths"

ls -Directory $MANIFEST_REPO | % {
	$m = ls $_ -Recurse -Include manifest.psd1 | Import-PowerShellDataFile | select Name, Version
	echo $m
	$items = ls $_
	$d = ni -Type Directory "$_\$($m.Version)"
	$items | % {Move-Item $_ $d}
}