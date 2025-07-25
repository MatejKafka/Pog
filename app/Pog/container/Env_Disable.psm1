using module ..\Utils.psm1
. $PSScriptRoot\..\header.ps1

# Pog.dll should be already loaded as an assembly, re-import it to access the cmdlets
# it is imported inside this module so that we can control what we expose to the package
Import-Module ([Pog.InternalState].Assembly.Location)

# this must NOT be an advanced funtion, otherwise we lose error message position from the manifest scriptblock
function __main {
    ### .SYNOPSIS
    ### This function is called after the container setup is finished to run the Disable script.
    param([Pog.PackageManifest]$Manifest)

    # invoke the entry point
    & (New-ContainerModule) $Manifest.Disable
}

Export-ModuleMember -Function __main -Cmdlet Remove-EnvVarEntry