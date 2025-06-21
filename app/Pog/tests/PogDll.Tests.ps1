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

$SharedSetup = {
    $PwshCmd = if ($PSVersionTable.PSEdition -eq "Core") {"pwsh"} else {"powershell"}

    ### Runs a new PowerShell instance with the specified test file.
    function InvokeIsolatedTest($FilePath) {
        $TestDir = [System.IO.Path]::GetFullPath("$env:TEMP\PogTests-$(New-Guid)")
        $PwshArgs = @("-noprofile", "-noninteractive", $FilePath, $TestDir)
        Write-Verbose "Test command: $PwshCmd $PwshArgs"

        $PSNativeCommandUseErrorActionPreference = $false
        $RawOutput = & $PwshCmd @PwshArgs
        rm -Recurse -Force $TestDir -ErrorAction Ignore
        if ($LastExitCode -ne 0) {
            throw "Test PowerShell invocation failed:`n$($RawOutput -join "`n")"
        }

        # replace temporary test directory path with TEST_DIR to get consistent output
        $SanitizedOutput = ($RawOutput -join "`n").Replace($TestDir, "TEST_DIR")
        Write-Verbose "Test output:`n$SanitizedOutput"
        return $SanitizedOutput
    }

    ### To save time, each tested cmdlet runs multiple tests in a single PowerShell session.
    ### This function parses the output back into separate outputs for each test.
    function ParseTestOutput([string]$Output) {
        $i = 0
        $Output.Replace("`r`n", "`n") -split "\n(?=--- .* ---(?:\n|$))" | % {
            $Name, $Content = $_ -split "`n", 2
            return @{
                I = $i++
                Name = $Name
                Content = $Content
            }
        }
    }
}

BeforeDiscovery $SharedSetup
BeforeAll $SharedSetup

Describe "<_>" -ForEach (ls -Directory $PSScriptRoot | % Name) {
    BeforeDiscovery {
        $Reference = (Get-Content $PSScriptRoot\$_\reference.txt) -join "`n"
        $RefBlocks = ParseTestOutput $Reference
    }

    # we emulate separate unit tests be running the test in `BeforeAll` and then just checking output
    #  for each section in the `It` tests below; one downside is that when the invocation fails,
    #  the error message is slightly less informative, but it still shows the full path of the test file
    BeforeAll {
        # dump the output to a file for easier manual comparison
        $Output = InvokeIsolatedTest $PSScriptRoot\$_\test.ps1 | Tee-Object $PSScriptRoot\$_\output.txt
        $OutBlocks = ParseTestOutput $Output
    }

    It "<Name> (block #<I>)" -ForEach $RefBlocks {
        $Out = $OutBlocks | select -Index $I
        if (-not $Out -or $Out.Name -ne $Name) {
            throw "Test output for '$Name' (block #$I) not found in the output."
        }
        $Out.Content | Should -Be $Content
    }

    # the -ForEach is a hack that allows us to pass a variable from `BeforeDiscovery` to `It`
    It "Verify output block count" -ForEach @($RefBlocks).Count {
        # only check -le, equality is effectively checked below
        $OutBlocks.Count | Should -BeLessOrEqual $_ -Because "reference and output should have the same number of blocks"
    }
}