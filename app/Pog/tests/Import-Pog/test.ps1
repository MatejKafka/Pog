using module ..\TestEnvironmentSetup.psm1
param([Parameter(Mandatory)][string]$TestDirPath)

. $PSScriptRoot\..\..\header.ps1


function title($Title) {
    Write-Host "--- $Title ---" -ForegroundColor White
}

function test($Sb) {
    $ErrorActionPreference = "Continue"
    $PSDefaultParameterValues = @{"Import-Pog:WhatIf" = $true; "Import-Pog:Force" = $true; "Import-Pog:ErrorAction" = "Continue"}
    Write-Host $Sb.ToString().Trim() -ForegroundColor Green
    try {
        & $Sb
    } catch [System.Management.Automation.ParameterBindingException] {
        Write-Host "PARAMETER BINDING EXCEPTION" -ForegroundColor Red
    } catch {
        Write-Host "TERMINATING ERROR: $_" -ForegroundColor Red
    }
    Write-Host ""
}

function TestCompletion($Text) {
    $Completions = [System.Management.Automation.CommandCompletion]::CompleteInput($Text, $Text.Length, $null).CompletionMatches | % CompletionText
    Write-Host "'$Text': $($Completions -join ", ")"
}


$TEST_DIR = SetupNewPogTestDir $TestDirPath

# setup package roots
$Roots = "$TEST_DIR\root0", "$TEST_DIR\root1"
CreatePackageRoots $Roots

# setup repository
CreateManifest test1 1.0.0
CreateManifest test1 2.0.0
CreateManifest test2 1.2.3

CreateImportedPackage $Roots[0] test1-imported test1 1.0.0
CreateImportedPackage $Roots[1] test2-imported test2 1.2.3


title "Package"
test {Import-Pog (Find-Pog test1)}
test {Import-Pog (Find-Pog test1, test2)}
test {Import-Pog (Find-Pog test1) -TargetName target}
test {Import-Pog (Find-Pog test1) -TargetPackageRoot $Roots[1]}
test {Import-Pog (Find-Pog test1) -TargetName target -TargetPackageRoot $Roots[1]}
test {Import-Pog (Find-Pog test1) (Get-Pog test1-imported)}
test {Find-Pog test1, test2 | Import-Pog}
test {Find-Pog test1, test2 | Import-Pog -TargetPackageRoot $Roots[1]}

title "Package, should fail"
test {Import-Pog (Find-Pog test1) -TargetPackageRoot "$TEST_DIR\nonexistent"}
test {Import-Pog (Find-Pog test1) -TargetName target, target2}
test {Import-Pog (Find-Pog test1, test2) -TargetName target}
test {Import-Pog (Find-Pog test1) (Get-Pog test1-imported, test1-imported)}
test {Import-Pog (Find-Pog test1, test2) (Get-Pog test1-imported)}
test {Find-Pog test1 | Import-Pog -TargetName target}
test {Find-Pog test1 | Import-Pog -Version 1.0.0}


title "PackageName"
test {Import-Pog test1}
test {Import-Pog test1, test2}
test {Import-Pog test1 -TargetName target}
test {Import-Pog test1 -TargetPackageRoot $Roots[1]}
test {Import-Pog test1 -TargetName target -TargetPackageRoot $Roots[1]}
test {Import-Pog test1 -Target (Get-Pog test1-imported)}
test {Import-Pog test1 1.0.0}
test {Import-Pog test1 1.0.0 -TargetName target}
test {Import-Pog test1 1.0.0 -TargetPackageRoot $Roots[1]}
test {Import-Pog test1 1.0.0 -TargetName target -TargetPackageRoot $Roots[1]}
test {Import-Pog test1 1.0.0 -Target (Get-Pog test1-imported)}
test {"test1", "test2" | Import-Pog}
test {"test1", "test2" | Import-Pog -TargetPackageRoot $Roots[1]}

title "PackageName, should fail"
test {Import-Pog test1, test2 -TargetName target}
test {Import-Pog test1 -TargetName test1, test2}
test {Import-Pog test1, test2 1.0.0}
test {Import-Pog -Version 1.0.0}
test {Import-Pog (Find-Pog test1) 1.0.0}


title "Target/TargetName"
test {Import-Pog -TargetName test1-imported}
test {Import-Pog -TargetName test1-imported, test2-imported}
test {Import-Pog -TargetName test1-imported -TargetPackageRoot $Roots[0]}
test {Import-Pog (Get-Pog test1-imported)}
test {Import-Pog (Get-Pog test1-imported, test2-imported)}
test {Get-Pog test1-imported, test2-imported | Import-Pog}


title "Completion"
# PackageName completion
TestCompletion "Import-Pog te"
# Version completion
TestCompletion "Import-Pog test1 "
# TargetName completion
TestCompletion "Import-Pog test1 -TargetName "