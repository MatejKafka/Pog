# Requires -Version 7
. $PSScriptRoot\header.ps1

Describe "Version parsing" {
	BeforeAll {
		function npv($v) {
			[Pog.PackageVersion]::new($v)
		}

		function TestArrayExact($a, $b) {
			$a | Should -Be $b
			# for some reason, PowerShell thinks that the local and returned [DevVersionType]
			#  are a different type, so we have to compare type names instead
			$a | % {$_.GetType()} | Should -Be ($b | % {$_.GetType()})
		}
	}

	It "parses PowerShell rc versions correctly" {
		$p = npv "7.1.0-rc5"
		TestArrayExact $p.Main @(7, 1, 0)
		TestArrayExact $p.Dev @([Pog.PackageVersion+DevVersionType]::Rc, 5)
	}

	It "parses Firefox development versions correctly" {
		$p = npv "89.0a1-2021-04-05"
		TestArrayExact $p.Main @(89, 0)
		TestArrayExact $p.Dev @([Pog.PackageVersion+DevVersionType]::Alpha, 1, 2021, 4, 5)
	}

	It "parses pypy versions correctly" {
		$p = npv "3.6-v3.7.1"
		TestArrayExact $p.Main @(3, 6)
		TestArrayExact $p.Dev @("v", 3, 7, 1)
	}
}

Describe "Version comparison" {
	BeforeAll {
		function npv($v) {
			[Pog.PackageVersion]::new($v)
		}

		function IsVersionGreater($V1, $V2) {
			return (npv $V1) -gt (npv $V2)
		}
	}

	It "correctly compares versions of different lengths" {
		IsVersionGreater "1.4.1" "1.4" | Should -BeTrue
		IsVersionGreater "1.4" "1.4.1" | Should -BeFalse
		IsVersionGreater "1.4.1-beta5" "1.4.1" | Should -BeFalse
		IsVersionGreater "1.4-beta2.1" "1.4-beta2" | Should -BeTrue
		# should compare as equal
		(npv "1.0").CompareTo((npv "1")) | Should -Be 0
	}

	It "correctly compares JetBrains style versions" {
		IsVersionGreater "2020.4.1" "2020.2.1" | Should -BeTrue
		IsVersionGreater "2020.2.1" "2020.4.1" | Should -BeFalse
		IsVersionGreater "2019.2.1" "2020.4.1" | Should -BeFalse
		IsVersionGreater "2020.2.2" "2020.2.2" | Should -BeFalse
	}

	It "correctly compares PowerShell versions" {
		IsVersionGreater "7.1.1" "7.1.0rc5" | Should -BeTrue
		# rc5 is earlier than release 7.1.0 version
		IsVersionGreater "7.1.0" "7.1.0rc5" | Should -BeTrue
		IsVersionGreater "7.1.0rc1" "7.1.0rc5" | Should -BeFalse
		IsVersionGreater "5.1.0" "7.0.0" | Should -BeFalse
		IsVersionGreater "1.2.0rc2" "7.1.0rc1" | Should -BeFalse
		IsVersionGreater "7.1.0rc2" "7.1.0rc1" | Should -BeTrue
	}

	It "correctly compares Firefox versions" {
		IsVersionGreater "78.0a2" "78.0a1" | Should -BeTrue
		IsVersionGreater "78.0b1" "78.0a1" | Should -BeTrue
		IsVersionGreater "78.0b1" "78.0a2" | Should -BeTrue
		IsVersionGreater "78.0" "78.0a2" | Should -BeTrue
	}

	It "correctly compares pypy versions" {
		IsVersionGreater "3.6-v3.7.1" "3.6-v4.0.0" | Should -BeFalse
		IsVersionGreater "3.6-v3.7.1" "3.6-v3.7.2" | Should -BeFalse
		IsVersionGreater "3.6-v3.7.1" "3.6-v3.7.1-b1" | Should -BeTrue
	}

	It "correctly compares 7zip versions" {
		IsVersionGreater "2107" "1900" | Should -BeTrue
		IsVersionGreater "2107" "2200" | Should -BeFalse
	}

	It "correctly compares 7zip dotted versions" {
		IsVersionGreater "21.07" "19.00" | Should -BeTrue
		IsVersionGreater "21.07" "22.00" | Should -BeFalse
	}

	It "correctly compares Wireshark development versions" {
		IsVersionGreater "3.7.0rc0-1634" "3.7.0rc0-1641" | Should -BeFalse
		IsVersionGreater "3.7.0rc0-1640" "3.7.0rc0-1636" | Should -BeTrue
	}
}
