param(
    $ReleaseId,
    $AssetName,
    $InFile,

    [securestring]
    $GitHubToken
)

Set-StrictMode -Version 3
$ErrorActionPreference = "Stop"

function api {
    Invoke-RestMethod @Args -Authentication Bearer -Token $GitHubToken | % {$_}
}

# need to list assets to find the correct asset ID
$CurrentAssets = api "https://api.github.com/repos/MatejKafka/Pog/releases/$ReleaseId/assets"

# delete the previous asset (we cannot directly overwrite an asset)
$CurrentAssets | ? name -eq $AssetName | % {
    Write-Host "Deleting existing asset '$($_.name)' (ID: $($_.id))..."
    $null = api $_.url -Method Delete
}

# upload the new asset
$Asset = api "https://uploads.github.com/repos/MatejKafka/Pog/releases/$ReleaseId/assets?name=$AssetName" `
    -InFile $InFile -ContentType application/zip
Write-Host "Uploaded new asset: $($Asset.browser_download_url)"
