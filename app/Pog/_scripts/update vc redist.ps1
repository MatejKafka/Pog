# NOTE: this script is used in CI
$SrcDir = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -find VC/Redist/MSVC/*/x64 -products *
if ($null -eq $SrcDir) {
    throw "Could not find VC Redistributable."
}

$Existing = [System.Collections.Generic.HashSet[string]](ls $PSScriptRoot\..\lib_compiled\vc_redist | % Name)
$null = $Existing.Remove("README.txt")
ls -Recurse -File $SrcDir `
    | % {$null = $Existing.Remove($_.Name); Write-Host $_; $_} `
    | cp -Destination $PSScriptRoot\..\lib_compiled\vc_redist\

$Existing | % {
    Write-Host "Not updated: $_"
}