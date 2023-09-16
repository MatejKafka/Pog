Describe "PogDll" {
    BeforeAll {
        $PwshCmd = if ($PSVersionTable.PSEdition -eq "Core") {"pwsh"} else {"powershell"}

        function InvokeWhatIfTest($FilePath) {
            # the test relies on the output from -WhatIf, which we cannot capture from inside powershell,
            #  so we have to run the test in a separate instance and compare the textual output
            $RawOutput = & $PwshCmd -noprofile -noninteractive $FilePath $TestDir
            # replace temporary test directory path with TEST_DIR to get consistent output
            $SanitizedOutput = ($RawOutput -join "`n").Replace($TestDir, "TEST_DIR")
            return $SanitizedOutput
        }
    }

    BeforeEach {
        $TestDir = mkdir "$($env:TEMP)\PogTests-$(New-Guid)"
    }

    AfterEach {
        rm -Recurse -Force $TestDir
    }

	It "Import-Pog" {
        $Reference = cat -Raw $PSScriptRoot\ImportPogTestInternal.reference.txt
        $Output = InvokeWhatIfTest $PSScriptRoot\ImportPogTestInternal.ps1

        $OutBlocks = $Output -split "\n(?=--- .* ---\n)"
        $RefBlocks = $Reference -split "\n(?=--- .* ---\n)"

        $OutBlocks.Count | Should -Be $RefBlocks.Count
        for ($i = 0; $i -lt $RefBlocks.Count; $i++) {
            $OutBlocks[$i] | Should -Be $RefBlocks[$i] -ErrorAction Continue
        }
	}
}
