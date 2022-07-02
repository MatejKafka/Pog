# Requires -Version 7
using module ..\..\Paths.psm1
using module ..\..\lib\Utils.psm1
# allows downloading files using BITS
using module BitsTransfer
. $PSScriptRoot\..\..\lib\header.ps1


# use Add-Type instead of direct enum definition, because those
#  are not visible when the module is imported
# FIXME: for some reason, this is very slow to evaluate
Add-Type @'
public enum UserAgentType {
	PowerShell,
	Browser,
	Wget
}
'@

<#
	Downloads the file from $SrcUrl to $TargetDir.

	BITS transfer (https://docs.microsoft.com/en-us/windows/win32/bits/about-bits)
	is used for download. Originally, Invoke-WebRequest was used in some cases,
	and it was potentially faster for small files for cold downloads (BITS service
	takes some time to initialize when not used for a while), but the added complexity
	does not seem to be worth it.
 #>
function DownloadFile($SrcUrl, $TargetDir, [UserAgentType]$UserAgent) {
	Write-Verbose "Downloading file from '$SrcUrl' to directory '$TargetDir'."

	# set BITS download arguments
	$BitsParams = @{
		Source = $SrcUrl
		Destination = $TargetDir
		Priority = if ($global:_Pog.InternalArgs.DownloadLowPriority) {"Low"} else {"Foreground"}
		Description = "Downloading file from '$SrcUrl' to '$TargetDir'..."
		# passing -Dynamic allows BITS to communicate with badly-mannered servers that don't
		#  support HEAD requests, Content-Length headers,...;
		#  see https://docs.microsoft.com/en-us/windows/win32/api/bits5_0/ne-bits5_0-bits_job_property_id),
		#  section BITS_JOB_PROPERTY_DYNAMIC_CONTENT
		Dynamic = $true
	}

	# user-agent override
	switch ($UserAgent) {
		PowerShell {}
		Browser {
			Write-Debug "Using fake browser (Firefox) user agent."
			$BitsParams.CustomHeaders = "User-Agent: " + [Microsoft.PowerShell.Commands.PSUserAgent]::FireFox
		}
		Wget {
			Write-Debug "Using fake wget user agent."
			$BitsParams.CustomHeaders = "User-Agent: Wget/1.20.3 (linux-gnu)"
		}
	}

	# download the file
	Start-BitsTransfer @BitsParams

	$DownloadedFile = ls $TargetDir
	# this should always hold
	if (@($DownloadedFile).Count -ne 1) {
		throw "Pog download cache entry directory contains multiple, or no files. This is an internal error," +`
			" it should never happen, and it seems Pog developers fucked something up. Plz send bug report."
	}

	Write-Debug "File downloaded (URL: $SrcUrl)."
	return $DownloadedFile
}

function GetFileHashWithProgressBar($File, $ProgressBarTitle = "Validating file hash") {
	function ShowProgress([int]$Percentage) {
		Write-Progress `
			-Activity $ProgressBarTitle `
			-PercentComplete $Percentage `
			-Completed:($Percentage -eq 100)
	}
	try {
		# TODO: figure out how to show actual progress
		ShowProgress 0
		return (Get-FileHash $File -Algorithm SHA256).Hash
	} finally {ShowProgress 100}
}

function RemoveCacheEntry($CacheEntryDirectory) {
	$MetadataFilePath = $CacheEntryDirectory + ".json"
	if (Test-Path $CacheEntryDirectory) {
		rm -Force -Recurse -LiteralPath $CacheEntryDirectory
	}
	if (Test-Path $MetadataFilePath) {
		rm -Force -LiteralPath $MetadataFilePath
	}
}

<#
	Adds the current package to the cache entry metadata file, or creates it if it doesn't exist.

	To avoid potential file name conflicts, the metadata file is not inside the cache entry directory,
	but at the same level, with the same name and an additional extension (so for entry at `CACHE_DIR\<hash>`,
	the metadata file will be located at `CACHE_DIR\<hash>.json`).

	LastWriteTime of the metadata file reflects the last time the cache entry was used.

	FIXME: The metadata file is currently updated on a best-effort basis, and it's possible that if some operation
	 fails, the content will not be up-to-date. Go through all code paths and figure out how to make it robust.
 #>
function SetCacheEntryMetadataFile($CacheEntryDirectory) {
	$MetadataFilePath = $CacheEntryDirectory + ".json"
	[array]$Sources = @()
	if (Test-Path $MetadataFilePath) {
		# load the current content
		$Content = Get-Content -Raw $MetadataFilePath
		try {
			$Sources = ConvertFrom-Json $Content -AsHashtable -NoEnumerate
		} catch {
			Write-Warning "Download cache entry metadata file is not a valid JSON file, deleting..." +`
					" (Path: '$MetadataFilePath')"
			rm -Force $MetadataFilePath
		}
	}

	$Manifest = $global:_Pog.Manifest
	$NewEntry = @{
		PackageName = $global:_Pog.PackageName
		PackageDirectory = [string]$global:_Pog.PackageDirectory
		ManifestName = if ($Manifest.ContainsKey("Name")) {$Manifest.Name} else {$null}
		ManifestVersion = if ($Manifest.ContainsKey("Version")) {$Manifest.Version} else {$null}
	}

	# check if the entry is already contained
	foreach ($Source in $Sources) {
		if (-not (Compare-Hashtable $NewEntry $Source)) {
			# our entry is already contained (probably reinstalling a package), nothing to do
			Write-Debug "Download cache entry metadata file already contains the current package."
			$File = Get-Item $MetadataFilePath
			# we used this cache entry, refresh last write time, even through the file did not change
			$File.LastWriteTime = Get-Date
			return
		}
	}
	# add the new entry
	Write-Debug "Updating download cache entry metadata file..."
	$Sources += $NewEntry
	$Sources | ConvertTo-Json -Depth 100 | Set-Content -Path $MetadataFilePath
}

function GetDownloadCacheEntryPath($Hash) {
	# each cache entry is a directory named with the SHA256 hash of target file,
	#  containing a single file with original name
	#  e.g. <DownloadCacheDir>/<sha-256>/app-v1.2.0.zip
	return Join-Path $PATH_CONFIG.DownloadCacheDir $Hash
}

function GetDownloadCacheEntry($Hash) {
	$DirPath = GetDownloadCacheEntryPath $Hash
	if (-not (Test-Path $DirPath)) {
		return $null
	}
	Write-Debug "Found matching download cache entry."

	# cache hit, validate file count
	$File = ls -File $DirPath
	if (@($File).Count -ne 1) {
		Write-Warning "Invalid download cache entry - contains multiple, or no items, erasing...: $Hash"
		RemoveCacheEntry $DirPath
		return $null
	}

	Write-Debug "Validating cache entry hash..."
	# validate file hash (to prevent tampering / accidental file corruption)
	$FileHash = GetFileHashWithProgressBar $File
	if ($Hash -ne $FileHash) {
		Write-Warning "Invalid download cache entry - content hash does not match, erasing...: $Hash"
		RemoveCacheEntry $DirPath
		return $null
	}
	Write-Debug "Cache entry hash validated."

	# update the metadata file
	SetCacheEntryMetadataFile $DirPath

	return $File
}

function DownloadFileToCache {
	param(
			[Parameter(Mandatory)]
			[string]
		$SrcUrl,
			<# SHA256 hash of expected file #>
			[Parameter(Mandatory)]
			[string]
		$ExpectedHash,
			[Hashtable]
		$DownloadParams = {}
	)

	$DirPath = GetDownloadCacheEntryPath $ExpectedHash.ToUpper()
	if (Test-Path $DirPath) {
		throw "Download cache already contains an entry for '$ExpectedHash' (from '$SrcUrl')."
	}
	# create the metadata file
	SetCacheEntryMetadataFile $DirPath

	try {
		$null = New-Item -Type Directory $DirPath
		Write-Debug "Created download cache dir for hash '$ExpectedHash'."
		$File = DownloadFile $SrcUrl $DirPath @DownloadParams

		$RealHash = GetFileHashWithProgressBar $File
		if ($ExpectedHash -ne $RealHash) {
			throw "Incorrect hash for file downloaded from $SrcUrl (expected : $ExpectedHash, real: $RealHash)."
		}
		Write-Debug "Hash check passed, file was correctly downloaded to cache."
		# hash check passed, return file reference
		return $File
	} catch {
		RemoveCacheEntry $DirPath
		throw
	}
}

<# Assumes the hash is correct. #>
function MoveFileToCache($File, $Hash) {
	$Hash = $Hash.ToUpper()
	$DirPath = GetDownloadCacheEntryPath $Hash

	# create the metadata file
	SetCacheEntryMetadataFile $DirPath

	if (Test-Path $DirPath) {
		# already populated, just delete the new file
		Write-Debug "Download cache already contains entry for hash '$Hash'."
		rm -LiteralPath $File
		return
	}

	Write-Debug "Moving file to download cache directory '$DirPath'."
	$null = New-Item -Type Directory $DirPath
	Move-Item $File $DirPath
}

Export function Get-UrlFileHash {
	param(
			[Parameter(Mandatory)]
			[string]
		$SrcUrl,
			[Parameter(Mandatory)]
			[hashtable]
		$DownloadParams,
			[switch]
		$ShouldCache
	)

	$TmpDir, $File = Invoke-TmpFileDownload $SrcUrl -DownloadParams $DownloadParams
	try {
		$Hash = GetFileHashWithProgressBar $File -ProgressBarTitle "Calculating file hash"
		if ($ShouldCache) {
			MoveFileToCache $File $Hash
		}
		return $Hash
	} finally {
		rm -Recurse $TmpDir
	}
}

Export function Invoke-TmpFileDownload {
	param(
			[Parameter(Mandatory)]
			[string]
		$SrcUrl,
			[Hashtable]
		$DownloadParams = @{}
	)

	# create unused tmp dir
	do {
		$TmpDirPath = Join-Path $PATH_CONFIG.DownloadTmpDir (New-Guid).Guid
		$TmpDir = New-Item -Type Directory $TmpDirPath -ErrorAction Ignore
	} while ($null -eq $TmpDir)

	# see last comment in DownloadFile for explanation of this weird try/finally construct
	$DownloadFinished = $false
	try {
		# we have a temp file with unique name and requested extension, download content
		$File = DownloadFile $SrcUrl $TmpDir @DownloadParams
		$DownloadFinished = $true
		return @($TmpDir, $File)
	} finally {
		if (-not $DownloadFinished) {
			rm -Recurse $TmpDir
		}
	}
}

Export function Invoke-CachedFileDownload {
	param(
			[Parameter(Mandatory)]
			[string]
		$SrcUrl,
			<# SHA256 hash of expected file #>
			[Parameter(Mandatory)]
			[string]
		$ExpectedHash,
			[Hashtable]
		$DownloadParams = @{}
	)

	Write-Debug "Checking if we have a cached copy for '$ExpectedHash'..."
	$CachedFile = GetDownloadCacheEntry $ExpectedHash
	if ($null -ne $CachedFile) {
		Write-Verbose "Found cached copy of requested file."
		return $CachedFile
	}

	Write-Verbose "Cached copy not found, downloading file to cache..."
	return DownloadFileToCache $SrcUrl $ExpectedHash -DownloadParams $DownloadParams
}
