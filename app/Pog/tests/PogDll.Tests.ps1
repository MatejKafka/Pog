[Diagnostics.CodeAnalysis.SuppressMessage("PSAvoidUsingCmdletAliases", "")]
param()

. $PSScriptRoot\..\header.ps1

Describe "PogDll" {
    BeforeAll {
        $PwshCmd = if ($PSVersionTable.PSEdition -eq "Core") {"pwsh"} else {"powershell"}

        function InvokeWhatIfTest($FilePath) {
            # the test relies on the output from -WhatIf, which we cannot capture from inside powershell,
            #  so we have to run the test in a separate instance and compare the textual output
            $PwshArgs = @("-noprofile", "-noninteractive", $FilePath, $TestDir.FullName)
            Write-Verbose "Test command: $PwshCmd $PwshArgs"

            $PSNativeCommandUseErrorActionPreference = $false
            $RawOutput = & $PwshCmd @PwshArgs
            if ($LastExitCode -ne 0) {
                throw "Test PowerShell invocation failed:`n$RawOutput"
            }

            # replace temporary test directory path with TEST_DIR to get consistent output
            $SanitizedOutput = ($RawOutput -join "`n").Replace($TestDir.FullName, "TEST_DIR")
            Write-Verbose "Test output:`n$SanitizedOutput"
            return $SanitizedOutput
        }

        function EvaluateWhatIfTest([string]$Output, [string]$Reference) {
            $Output = $Output.Replace("`r`n", "`n")
            $Reference = $Reference.Replace("`r`n", "`n")

            $OutBlocks = $Output -split "\n(?=--- .* ---\n)"
            $RefBlocks = $Reference -split "\n(?=--- .* ---\n)"

            $OutBlocks.Count | Should -Be $RefBlocks.Count
            for ($i = 0; $i -lt $RefBlocks.Count; $i++) {
                # the single-line diff rendered by Pester is pretty hard to read, so we render our own diff
                if ($OutBlocks[$i] -cne $RefBlocks[$i]) {
                    Write-Host "Diff for block #${i}:"
                    Write-Host ([Pog.Utils.DiffRenderer]::RenderDiff($RefBlocks[$i], $OutBlocks[$i]))
                }
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

    It "<_>" -ForEach (ls -Directory $PSScriptRoot | % Name) {
        $Output = InvokeWhatIfTest $PSScriptRoot\$_\test.ps1
        EvaluateWhatIfTest $Output (cat -Raw $PSScriptRoot\$_\reference.txt)
    }
}