param(
        [Parameter(Mandatory)]
        [string]
    $RemoteRepoDir,
        [Parameter(Mandatory)]
        [string]
    $SourceRepoDir,
        [switch]
    $Validate
)

Set-StrictMode -Version 3
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

Import-Module Pog
# use the local repository, instead of the default remote repo
Set-PogRepository $SourceRepoDir

if ($Validate) {
    # validate the input repository before building, allow missing hashes
    if (-not (Confirm-PogRepository -IgnoreMissingHash)) {
      throw "Input repository validation failed (see warnings above)."
    }
}


$null = mkdir -Force $RemoteRepoDir
cd $RemoteRepoDir

rm -Recurse *

$Packages = [array][Pog.InternalState]::Repository.Enumerate()

$VersionMap = [ordered]@{}
$Packages | % {
    $VersionMap[$_.PackageName] = @($_.EnumerateVersions() | % ToString)
}
# not really html, but whatever
$VersionMap | ConvertTo-Json -Depth 100 -Compress > index.html

# TODO: build in parallel
$TmpPackage = New-PogPackage _zip_export
try {
    $Packages | % {
        $null = mkdir $_.PackageName
        $VersionCounter = 0
        $_.Enumerate() | % {
            $_.ImportTo($TmpPackage)
            Compress-Archive "$($TmpPackage.Path)\*" ".\$($_.PackageName)\$($_.Version).zip"
            $VersionCounter++
        }

        [pscustomobject]@{
            PackageName = $_.PackageName
            VersionCount = $VersionCounter
        }
    }
} finally {
    rm -Force -Recurse $TmpPackage.Path
}