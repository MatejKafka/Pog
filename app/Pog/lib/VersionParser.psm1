# Requires -Version 7
. $PSScriptRoot\header.ps1


function PadArray([array]$arr, $length, $filler) {
	while (@($Arr).Count -lt $length) {
		$Arr += @($filler)
	}
	return $Arr
}

function Zip($a1, $a2, $filler = $null) {
	$l = [math]::Max(@($a1).Count, @($a2).Count)
	$a1 = PadArray $a1 $l $filler
	$a2 = PadArray $a2 $l $filler
	for ($i = 0; $i -lt $l; $i += 1) {
		@{0 = $a1[$i]; 1 = $a2[$i]}
	}
}

enum DevVersionType {
	Nightly = 0
	Preview = 1
	Alpha = 2
	Beta = 3
	Rc = 4
}
$DevVersionTypeMap = @{
	nightly = [DevVersionType]::Nightly
	preview = [DevVersionType]::Preview
	alpha = [DevVersionType]::Alpha
	a = [DevVersionType]::Alpha
	beta = [DevVersionType]::Beta
	b = [DevVersionType]::Beta
	rc = [DevVersionType]::Rc
}

# https://github.com/PowerShell/PowerShell/issues/10669
<# Used to parse and represent package versions. There's quite a lot of heuristics here,
   but it behaves sanely for all the version formats I encountered yet. #>
class PogPackageVersion : System.IComparable, System.IEquatable[Object] {
	<# Main part of the version – dot-separated numbers, not necessarily semver. #>
	[int[]]$Main
	<# Development version suffix – "beta.1", "preview-1.2.3",... #>
	[array]$Dev
	<# The original unchanged version string. #>
	[string]$VersionString

	PogPackageVersion([string]$VersionString) {
		$this.VersionString = $VersionString

		$VReg = "^(?<Main>\d[\.\d]*)(?<Dev>.*)$"
		if ($VersionString -notmatch $VReg) {
			throw "Could not parse package version: " + $VersionString
		}

		$this.Main = $Matches.Main.Split(".") | ? {$_} | % {[int]::Parse($_)}

		$this.Dev = @()
		$IsNumericToken = $null
		[string]$Token = ""
		function Flush {
			if ($Token -eq "") {return}
			$this.Dev += if ($IsNumericToken) {[int]::Parse($Token)}
				elseif ($DevVersionTypeMap.ContainsKey($Token)) {$DevVersionTypeMap[$Token]}
				else {[string]$Token}
		}
		$Matches.Dev.ToCharArray() | % {
			if ($_ -in ".", "-", "_") {
				# split on these chars
				Flush
				$IsNumericToken = $null
				$Token = ""
			} elseif ([char]::IsDigit($_) -eq $IsNumericToken) {
				$Token += $_
			} else {
				Flush
				$IsNumericToken = [char]::IsDigit($_)
				$Token = $_
			}
		}
		Flush
	}

	[string] ToString() {
		return $this.VersionString
	}

	[int] CompareTo($Version2) {
		$V1 = $this
		$V2 = [PogPackageVersion]$Version2
		# compare the main (semi-semver) part
		foreach ($_ in (Zip $V1.Main $V2.Main)) {
			# if one of the versions is shorter than the other, treat the extra fields as zeros
			$P1 = if ($null -eq $_[0]) {0} else {$_[0]}
			$P2 = if ($null -eq $_[1]) {0} else {$_[1]}
			if ($P1 -eq $P2) {continue}
			return $P1.CompareTo($P2)
		}

		# same semi-semvers, no dev suffix, the versions are equal
		if ($V1.Dev.Count -eq 0 -and $V2.Dev.Count -eq 0) {return 0}
		# V2 is dev version of V1 -> V1 is greater
		if ($V1.Dev.Count -eq 0) {return 1}
		# V1 is dev version of V2 -> V2 is greater
		if ($V2.Dev.Count -eq 0) {return -1}

		# both have dev suffix and the same semi-semver, compare dev suffixes
		foreach ($_ in (Zip $V1.Dev $V2.Dev)) {
			# here's the fun part, because the dev suffixes are quite free-style
			# each possible version field type has an internal ordering
			# if both fields have a different type, the following priorities are used:
			#  string < DevVersionType < null < int, where later values are considered greater than earlier ones
			#  effectively:
			#   - $null is treated as -1 (int)
			#   - DevVersionType vs string – could be basically anything, assume that DevVersionType is newer
			#   - DevVersionType vs int – assume that int is a newer version ("almost-release"?)
			#   - int vs string – assume that int is a newer version ("almost-release", there are no qualifiers)

			$P1 = if ($null -eq $_[0]) {-1} else {$_[0]}
			$P2 = if ($null -eq $_[1]) {-1} else {$_[1]}

			if ($P1 -eq $P2) {continue}

			$TypeOrder = @{[string] = 0; [DevVersionType] = 1; [int] = 2}
			$O1 = $TypeOrder[$P1.GetType()]
			$O2 = $TypeOrder[$P2.GetType()]
			if ($O1 -ne $O2) {
				# different field types, compare based on the field type ordering above
				return $O1.CompareTo($O2)
			} else {
				# same field types, use the default comparator for the type
				return $P1.CompareTo($P2)
			}
		}
		# both Main and Dev parts are equal
		return 0
	}

	[bool] Equals($Version2) {
		if ($Version2 -isnot [PogPackageVersion]) {
			return $false
		}
		return $this.CompareTo($Version2) -eq 0
	}
}

Export function New-PackageVersion {
	[OutputType([PogPackageVersion])]
	param(
			[Parameter(Mandatory)]
			[string]
		$VersionString
	)
	return [PogPackageVersion]::new($VersionString)
}

Export function Get-LatestVersion {
	[OutputType([string])]
	param(
			[Parameter(Mandatory)]
			[string[]]
		$Versions
	)

	if ($Versions.Count -eq 0) {
		throw "Passed version list is empty."
	}

	$ParsedVersions = $Versions | % {[PogPackageVersion]$_}
	$Max = $ParsedVersions[0]
	$ParsedVersions | select -Skip 1 | % {
		if ($_ -gt $Max) {
			$Max = $_
		}
	}
	return $Max.ToString()
}
