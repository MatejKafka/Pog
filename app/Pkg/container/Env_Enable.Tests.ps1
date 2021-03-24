Import-Module $PSScriptRoot\Env_Enable.psm1	

# FIXME: this doesn't trigger init and cleanup, which might be an issue

Describe "Set-SymlinkedPath" {
	BeforeAll {
		cd TestDrive:
	}
	
	AfterAll {
		Set-Location D:
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
		
		Set-SymlinkedPath .\src .\target -Directory -Merge
		
		(Get-Item .\src).LinkType | Should -Be "SymbolicLink"
		(Get-Item .\src).Target | Should -Be ".\target"
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
		
		Set-SymlinkedPath .\src .\target -Directory
		
		(Get-Item .\src).LinkType | Should -Be "SymbolicLink"
		(Get-Item .\src).Target | Should -Be ".\target"
		(ls .\target).Name | Should -BeExactly @("dir1", "target_only")
		(ls .\target\dir1).Name | Should -BeExactly @("original")
	}
	
	It "-Merge behaves correctly when source is a symlink" {
		mkdir target/target_only
		mkdir target/dir1
		ni target/dir1/original
		rm src
		$null = cmd /C mklink /D .\src .\target
		
		Set-SymlinkedPath .\src .\target -Directory
		
		(Get-Item .\src).LinkType | Should -Be "SymbolicLink"
		(Get-Item .\src).Target | Should -Be ".\target"
		(ls .\target).Name | Should -BeExactly @("dir1", "target_only")
		(ls .\target\dir1).Name | Should -BeExactly @("original")
	}
}