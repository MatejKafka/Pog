# Requires -Version 7
BeforeAll {
	$module = Import-Module -Force $PSScriptRoot\VersionParser -PassThru
}

Describe "Version parsing" {
	BeforeAll {
		$function:ParseVersion = & $module {$function:ParseVersion}
	}

	AfterAll {
		Remove-Item Function:ParseVersion
	}
	
	It "parses PowerShell rc versions correctly" {
		$p = ParseVersion "7.1.0-rc5"
		$p.Main | Should -Be @(7, 1, 0)
		$p.Dev | Should -Be @("rc", 5)
		$p.Dev | % {$_.GetType()} | Should -Be @([string], [int])
	}
	
	It "parses Firefox versions correctly" {
		$p = ParseVersion "78.0a2"
		$p.Main | Should -Be @(78, 0)
		$p.Dev | Should -Be @("a", 2)
		$p.Dev | % {$_.GetType()} | Should -Be @([string], [int])
	}
	
	It "parses pypy versions correctly" {
		$p = ParseVersion "3.6-v3.7.1"
		$p.Main | Should -Be @(3, 6)
		$p.Dev | Should -Be @("v", 3, ".", 7, ".", 1)
		$p.Dev | % {$_.GetType()} | Should -Be @([string], [int], [string], [int], [string], [int])	
	}
}

Describe "Version comparison" {
	BeforeAll {
		$function:IsVersionGreater = & $module {$function:IsVersionGreater}
	}

	AfterAll {
		Remove-Item Function:IsVersionGreater
	}

	It "correctly compares versions of different lengths" {
		IsVersionGreater "1.4.1" "1.4" | Should -BeTrue
		IsVersionGreater "1.4" "1.4.1" | Should -BeFalse
		IsVersionGreater "1.4.1-beta5" "1.4.1" | Should -BeFalse
		IsVersionGreater "1.4-beta2.1" "1.4-beta2" | Should -BeTrue
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
		
		# technically, this should be true, but we cannot reliably expand
		#  the comparison to multiple dash-separated groups without breaking
		#  on some dev suffixes
		IsVersionGreater "3.6-v3.7.1" "3.6-v3.7.1-b1" | Should -BeFalse
	}

	It "correctly compares 7zip versions" {
		IsVersionGreater "2107" "1900" | Should -BeTrue
		IsVersionGreater "2107" "2200" | Should -BeFalse
	}

	It "correctly compares Wireshark development versions" {
		IsVersionGreater "3.7.0rc0-1634" "3.7.0rc0-1641" | Should -BeFalse
		IsVersionGreater "3.7.0rc0-1640" "3.7.0rc0-1636" | Should -BeTrue
	}
}
