param($Test = $null, [switch]$Debug, [switch]$PowerShell5)

if ("PogTests:\" -eq $PWD) {
    throw "Refusing to run nested test, first exit the previous session."
}

$PwshCmd = if ($PowerShell5) {"powershell"} else {"pwsh"}
$PogDllPath = if ($Debug) {
    "$PSScriptRoot\..\lib_compiled\Pog\bin\Debug\netstandard2.0\Pog.dll"
} else {
    "$PSScriptRoot\..\lib_compiled\Pog.dll"
}

$TestDir = mkdir "$($env:TEMP)\PogTests-$(New-Guid)"
try {
    & $PwshCmd -NoExit -Args $PSScriptRoot, $Test, ([bool]$Debug), $TestDir {
        param($PSScriptRoot, $Test, $Debug)

        if ($Debug) {
            # load the debug version of Pog.dll
            $env:POG_DEBUG = "1"
        }

        if ($Test) {
            . $PSScriptRoot\..\tests\$Test\test.ps1 @Args
        } else {
            . $PSScriptRoot\..\tests\SetupTestEnvironment.ps1 @Args
        }
    }
} finally {
    rm -Recurse -Force $TestDir
}