param([Parameter(Mandatory)]$RemoteRepoDir)

$env:POG_DEBUG = "1"; Import-Module Pog

cd $RemoteRepoDir

rm -Recurse *

$Packages = [array][Pog.InternalState]::Repository.Enumerate()

$VersionMap = [ordered]@{}
$Packages | % {
    $VersionMap[$_.PackageName] = @($_.EnumerateVersions() | % ToString)
}
# not really html, but whatever
$VersionMap | ConvertTo-Json -Depth 100 -Compress > index.html

$TmpPackage = New-PogImportedPackage _zip_export
try {
    $Packages | % {
        $null = mkdir $_.PackageName
        $_.Enumerate() | % {
            $_.ImportTo($TmpPackage)
            Compress-Archive "$($TmpPackage.Path)\*" ".\$($_.PackageName)\$($_.Version).zip"
            echo $_.Path
        }
    }
} finally {
    rm -Force -Recurse $TmpPackage.Path
}