### Copy vcredist DLLs from the local MSVC installation.
param(
        [Parameter(Mandatory)]
        [string]
    $OutDir
)

$SrcDir = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -find VC/Redist/MSVC/*/x64 -products *
if ($null -eq $SrcDir) {
    throw "Could not find VC Redistributable."
}

$Existing = [System.Collections.Generic.HashSet[string]](ls $OutDir -Filter *.dll | % Name)
ls -Recurse -File $SrcDir `
    | % {$null = $Existing.Remove($_.Name); Write-Host $_; $_} `
    | cp -Destination $OutDir -Force

$Existing | % {
    Write-Host "Not updated: $_"
}