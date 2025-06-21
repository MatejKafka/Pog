. $PSScriptRoot\header.ps1

# check if Pog.dll is already loaded in the current PowerShell process
$PogSource = try {[Pog.InternalState].Assembly.Location} catch {}
if ($PogSource -and -not $PogSource.StartsWith($PSScriptRoot)) {
    throw ("Conflicting Pog.dll from another location already loaded from '$PogSource', " + `
            "loading multiple copies of Pog in a single PowerShell session is not supported.")
}

if (-not $PogSource) {
    # Pog.dll not yet loaded
    if (-not (Test-Path Env:POG_DEBUG)) {
        $PogSource = "$PSScriptRoot\lib_compiled\Pog.dll"
    } else {
        # load debug build of the compiled library (only available in local builds)
        $PogSource = "$PSScriptRoot\lib_compiled\Pog\bin\Debug\netstandard2.0\Pog.dll"
    }
}

# even if Pog.dll is loaded as an assembly, we re-import it to access the cmdlets
Import-Module $PogSource
