BeforeAll {
	Import-Module -Force $PSScriptRoot\Utils.psm1
}

Describe "DynamicParams" {
	BeforeAll {
		function TestFn {
			[CmdletBinding()]
			param(
					[Parameter(Mandatory)]
				$Param1,
					[Parameter(Mandatory)]
				$Param2,
					[Parameter(Mandatory)]
				$Param3
			)
			
			dynamicparam {
				return New-DynamicSwitchParam "SwitchParam"
			}
			
			begin {
				echo $PSBoundParameters
			}
		}
	}

	# broken: https://github.com/PowerShell/PowerShell/issues/13771
	It "parsed parameter order is correct" {
		$params = TestFn -SwitchParam "test1" "test2" "test3"
		
		$params.Keys | Should -HaveCount 4
		$params.SwitchParam | Should -Be $true
		@($params.Param1, $params.Param2, $params.Param3) | Should -BeExactly @("test1", "test2", "test3")
	}
}