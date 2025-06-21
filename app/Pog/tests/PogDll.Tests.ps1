### .SYNOPSIS
### This directory contains a set of integration tests for some of the public Pog commands.
###
### .DESCRIPTION
### Since Pog is using a process-wide internal state and we need to override most of the data
### directories to not touch actual user data, the tests work by invoking a new pwsh instance,
### running a series of tests and comparing the stdout with a reference output. Another reason
### for using this approach is that some tests are evaluated using -WhatIf output, which cannot
### be easily captured from inside the same pwsh instance.
###
### This file itself should not load Pog.dll, so it should be safe to rebuild it and re-run
### the tests without having to exit the PowerShell session.
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
                throw "Test PowerShell invocation failed:`n$($RawOutput -join "`n")"
            }

            # replace temporary test directory path with TEST_DIR to get consistent output
            $SanitizedOutput = ($RawOutput -join "`n").Replace($TestDir.FullName, "TEST_DIR")
            Write-Verbose "Test output:`n$SanitizedOutput"
            return $SanitizedOutput
        }

        function EvaluateWhatIfTest([string]$Output, [string]$Reference) {
            $Output = $Output.Replace("`r`n", "`n")
            $Reference = $Reference.Replace("`r`n", "`n")

            $OutBlocks = $Output -split "\n(?=--- .* ---(\n|$))"
            $RefBlocks = $Reference -split "\n(?=--- .* ---(\n|$))"

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

    It "<_>" -ForEach (ls -Directory $PSScriptRoot | % Name) {
        $Output = InvokeWhatIfTest $PSScriptRoot\$_\test.ps1
        # also dump the output to a file for easier manual comparison
        Set-Content $PSScriptRoot\$_\output.txt $Output -NoNewline
        EvaluateWhatIfTest $Output (Get-Content -Raw $PSScriptRoot\$_\reference.txt)
    }
}