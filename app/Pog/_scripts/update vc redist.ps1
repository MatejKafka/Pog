$SrcDir = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -find VC/Redist/MSVC/*/x64
if ($null -eq $SrcDir) {
    throw "Could not find VC Redistributable."
}
ls -Recurse -File $SrcDir | % {Write-Host $_; $_} | cp -Destination $PSScriptRoot\..\lib_compiled\vc_redist\