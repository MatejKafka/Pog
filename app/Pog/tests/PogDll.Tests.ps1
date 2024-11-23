Describe "PogDll" {
    BeforeAll {
        $PwshCmd = if ($PSVersionTable.PSEdition -eq "Core") {"pwsh"} else {"powershell"}

        function InvokeWhatIfTest($FilePath) {
            # the test relies on the output from -WhatIf, which we cannot capture from inside powershell,
            #  so we have to run the test in a separate instance and compare the textual output
            $PwshArgs = @("-noprofile", "-noninteractive", $FilePath, $TestDir.FullName)
            Write-Verbose "Test command: $PwshCmd $PwshArgs"

            $RawOutput = & $PwshCmd @PwshArgs
            # replace temporary test directory path with TEST_DIR to get consistent output
            $SanitizedOutput = ($RawOutput -join "`n").Replace($TestDir.FullName, "TEST_DIR")

            Write-Verbose "Test output:`n$SanitizedOutput"
            return $SanitizedOutput
        }

        function EvaluateWhatIfTest($Output, $Reference) {
            $Output = $Output.Replace("`r`n", "`n")
            $Reference = $Reference.Replace("`r`n", "`n")

            $OutBlocks = $Output -split "\n(?=--- .* ---\n)"
            $RefBlocks = $Reference -split "\n(?=--- .* ---\n)"

            $OutBlocks.Count | Should -Be $RefBlocks.Count
            for ($i = 0; $i -lt $RefBlocks.Count; $i++) {
                $OutBlocks[$i] | Should -Be $RefBlocks[$i] -Because "block $i" -ErrorAction Continue
            }
        }
    }

    BeforeEach {
        $TestDir = mkdir "$($env:TEMP)\PogTests-$(New-Guid)"
    }

    AfterEach {
        rm -Recurse -Force $TestDir
    }

	It "Import-Pog" {
        $Output = InvokeWhatIfTest $PSScriptRoot\Import-Pog\test.ps1
        EvaluateWhatIfTest $Output (cat -Raw $PSScriptRoot\Import-Pog\reference.txt)
	}

    It "Export-Command" {
        $Output = InvokeWhatIfTest $PSScriptRoot\Export-Command\test.ps1
        EvaluateWhatIfTest $Output (cat -Raw $PSScriptRoot\Export-Command\reference.txt)
    }

    It "Export-Shortcut" {
        $Output = InvokeWhatIfTest $PSScriptRoot\Export-Shortcut\test.ps1
        EvaluateWhatIfTest $Output (cat -Raw $PSScriptRoot\Export-Shortcut\reference.txt)
    }
}
