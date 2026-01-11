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

$OutDir = mkdir -Force $RemoteRepoDir
rm -Recurse $OutDir\*
$OutV1, $OutV2 = mkdir $OutDir\v1, $OutDir\v2

$Packages = [array][Pog.InternalState]::Repository.Enumerate()

$VersionMap = [ordered]@{}
$Packages | % {
    $VersionMap[$_.PackageName] = @($_.EnumerateVersions() | % ToString)
}
# not really html, but whatever
$VersionMap | ConvertTo-Json -Depth 100 -Compress | Set-Content "$OutV1\index.html", "$OutV2\index.html"

$TmpPackage = New-PogPackage _remote_repo_zip_export
try {
    # TODO: run in parallel
    $Packages | % {
        $null = mkdir "$OutV1\$($_.PackageName)"
        $null = mkdir "$OutV2\$($_.PackageName)"

        $VersionCounter = 0
        $_.Enumerate() | % {
            $VersionCounter++
            # v1
            $_.ImportTo($TmpPackage)
            Compress-Archive "$($TmpPackage.Path)\*" "$OutV1\$($_.PackageName)\$($_.Version).zip"
            # v2
            Set-Content "$OutV2\$($_.PackageName)\$($_.Version).psd1" $_.Manifest -NoNewline
        }

        [pscustomobject]@{
            PackageName = $_.PackageName
            VersionCount = $VersionCounter
        }
    }
} finally {
    rm -Force -Recurse $TmpPackage.Path
}

# copy over v1 to the root, older Pog versions expect to find the repo in the root
# since packages are checked against the top-level listing, the `v1` and `v2` dirs should not be confused for packages
cp -Recurse $OutV1\* $OutDir