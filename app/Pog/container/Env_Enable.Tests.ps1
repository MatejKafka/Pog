[Diagnostics.CodeAnalysis.SuppressMessage("PSAvoidUsingCmdletAliases", "")]
[Diagnostics.CodeAnalysis.SuppressMessage("PSUseDeclaredVarsMoreThanAssignments", "")]
param()

BeforeAll {
	$Module = Import-Module -Force $PSScriptRoot\Env_Enable.psm1 -PassThru
	# reset possible globally set values
	$script:PSDefaultParameterValues = @{}
	$OrigInformationPreference = $global:InformationPreference
	$global:InformationPreference = "SilentlyContinue"
}

AfterAll {
	$global:InformationPreference = $OrigInformationPreference
	Remove-Variable OrigInformationPreference
	Remove-Module $Module
	Remove-Variable Module
}

# FIXME: this doesn't trigger init and cleanup, which might be an issue

Describe "New-Symlink" {
	BeforeAll {
		Push-Location TestDrive: -StackName PogTests
	}

	AfterAll {
		Pop-Location -StackName PogTests
	}

	BeforeEach {
		mkdir src
		mkdir target
	}

	AfterEach {
		rm -Recurse src
		rm -Recurse target
	}

	It "merges existing directories" {
		mkdir target/dir1
		ni target/dir1/original
		mkdir src/dir1
		ni src/dir1/new_file
		mkdir src/src_only
		mkdir target/target_only

		New-Symlink .\src .\target Directory -Merge

		(Get-Item .\src).LinkType | Should -Be "SymbolicLink"
		(Get-Item .\src).Target | Should -Be "target"
		(ls .\target).Name | Should -BeExactly @("dir1", "src_only", "target_only")
		(ls .\target\dir1).Name | Should -BeExactly @("new_file")
	}

	It "overwrites directories when -Merge is not passed" {
		mkdir target/dir1
		ni target/dir1/original
		mkdir src/dir1
		ni src/dir1/new_file
		mkdir src/src_only
		mkdir target/target_only

		New-Symlink .\src .\target Directory

		(Get-Item .\src).LinkType | Should -Be "SymbolicLink"
		(Get-Item .\src).Target | Should -Be "target"
		(ls .\target).Name | Should -BeExactly @("dir1", "target_only")
		(ls .\target\dir1).Name | Should -BeExactly @("original")
	}

	It "-Merge behaves correctly when source is a symlink" {
		mkdir target/target_only
		mkdir target/dir1
		ni target/dir1/original
		rm src
		$null = cmd /C mklink /D .\src target

		New-Symlink .\src .\target Directory

		(Get-Item .\src).LinkType | Should -Be "SymbolicLink"
		(Get-Item .\src).Target | Should -Be "target"
		(ls .\target).Name | Should -BeExactly @("dir1", "target_only")
		(ls .\target\dir1).Name | Should -BeExactly @("original")
	}
}