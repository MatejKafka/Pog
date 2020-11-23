. $PSScriptRoot\header.ps1


function PadArray([array]$arr, $length, $filler) {
	while ($Arr.Count -lt $length) {
		$Arr += @($filler)
	}
	return $Arr
}

function Zip($a1, $a2, $filler = $null) {
	$l = [math]::Max($a1.Count, $a2.Count)
	$a1 = PadArray $a1 $l $filler
	$a2 = PadArray $a2 $l $filler
	for ($i = 0; $i -lt $l; $i += 1) {
		@{0 = $a1[$i]; 1 = $a2[$i]}
	}
}

function ParseVersion($V) {
	$VReg = "^(?<Main>\d[\.\d]+)-?(?<Dev>.*)$"
	if ($V -notmatch $VReg) {
		throw "Could not parse package version: " + $V
	}
	
	$ParsedMain = $Matches.Main.Split(".") | ? {$_} | % {[int]::Parse($_)}
	
	$ParsedDev = @()
	$cType = $null
	$c = ""
	$Matches.Dev.ToCharArray() | % {
		if ([char]::IsDigit($_) -eq $cType) {
			$c += $_
		} else {
			if ($c -ne "") {$ParsedDev += if ($cType) {[int]::Parse($c)} else {"" + $c}}
			$cType = [char]::IsDigit($_)
			$c = $_
		}
	}
	if ($c -ne "") {$ParsedDev += if ($cType) {[int]::Parse($c)} else {"" + $c}}
	
	return @{
		Main = $ParsedMain
		Dev = $ParsedDev
	}
}

function IsVersionGreater($V1, $V2) {
	$P1 = ParseVersion $V1
	$P2 = ParseVersion $V2

	# compare semver part
	foreach ($_ in (Zip $P1.Main $P2.Main)) {
		if ($_[0] -eq $_[1]) {continue}
		return $_[0] -gt $_[1]
	}
	
	# same semvers, no dev suffix
	if ($P1.Dev.Count -eq 0 -and $P2.Dev.Count -eq 0) {return $false}
	# V2 is dev version of V1 -> V1 is greater
	if ($P1.Dev.Count -eq 0) {return $true}
	# V1 is dev version of V2 -> V2 is greater
	if ($P2.Dev.Count -eq 0) {return $false}
	
	# both have dev suffix and same semver, compare dev suffix
	foreach ($_ in (Zip $P1.Dev $P2.Dev)) {
		if ($_[0] -eq $_[1]) {continue}
		return $_[0] -gt $_[1]
	}
	return $false
}

Export function Get-LatestVersion {
	param(
			[Parameter(Mandatory)]
			[string[]]
		$Versions
	)
	
	if ($Versions.Count -eq 0) {
		throw "Passed version list is empty."
	}
	
	$Max = $Versions[0]
	$Versions | select -Skip 1 | % {
		if (IsVersionGreater $_ $Max) {
			$Max = $_
		}
	}
	return $Max
}
