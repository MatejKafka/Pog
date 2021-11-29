# Requires -Version 7
. $PSScriptRoot\..\..\lib\header.ps1
Import-Module $PSScriptRoot\..\..\Paths

# allows downloading files using BITS
Import-Module BitsTransfer


# use Add-type instead of direct enum definition, because those
#  are not visible when the module is imported
Add-Type @'
public enum UserAgentType {
	PowerShell,
	Browser,
	Wget
}
'@

function DownloadFile($SrcUrl, $TargetDir, [UserAgentType]$UserAgent) {
	Write-Debug "Downloading file from '$SrcUrl' to directory '$TargetDir'."

	# in case other parameters are added, figure out if they can be passed to Start-BitsTransfer, or just Invoke-WebRequest
	$Params = @{}
	switch ($UserAgent) {
		PowerShell {}
		Browser {
			Write-Debug "Using fake browser user agent."
			$Params.UserAgent = [Microsoft.PowerShell.Commands.PSUserAgent]::FireFox
		}
		Wget {
			Write-Debug "Using fake wget user agent."
			$Params.UserAgent = "Wget/1.20.3 (linux-gnu)"
		}
	}

	# first, find the real download URL and some useful metadata
	$Res = Invoke-WebRequest -Method Head $SrcUrl @Params
	$RealUrl = $Res.BaseResponse.RequestMessage.RequestUri

	# try to get the file name from Content-Disposition header, fallback to last segment of original URL
	$FileName = if ($Res.Headers.ContainsKey("Content-Disposition")) {
		[Net.Http.Headers.ContentDispositionHeaderValue]::Parse($Res.Headers.'Content-Disposition').FileName -replace '"', ""
	} else {$null}

	# FIXME: it's possible that the last segment of the URL is also empty (e.g. https://domain/dir_but_actually_archive/),
	#  and then this fallback would also fail
	if ([string]::IsNullOrWhiteSpace($FileName)) {
		$FileName = ([uri]$SrcUrl).Segments[-1] # fallback
	}

	$TargetPath = Join-Path $TargetDir $FileName

	# we can use two different ways to download the file: BITS transfer, or direct download with Invoke-WebRequest
	# BITS transfer has multiple advantages (better progress reporting, much faster, better error cleanup, priorities),
	#  but it doesn't support custom HTTP User-Agent
	# therefore, we use Invoke-WebRequest when custom User-Agent is set, and BITS for all other cases
	if (-not $Params.ContainsKey("UserAgent")) {
		# we can use BITS
		Write-Debug "Downloading file using BITS..."
		$Description = "Downloading file from '$SrcUrl' to '$TargetPath'..."
		$Priority = if ($global:_InternalArgs.DownloadLowPriority) {"Low"} else {"Foreground"}
		Start-BitsTransfer $RealUrl -Destination $TargetPath -Priority $Priority -Description $Description
	} else {
		# we have to use Invoke-WebRequest, non-default user agent is required
		Write-Debug "Downloading file using Invoke-WebRequest..."
		if ($global:_InternalArgs.DownloadLowPriority) {
			Write-Debug ("Ignoring -LowPriority download flag, because a custom user agent was requested" + `
					" when calling Install-FromUrl, which is not available with BITS transfers yet.")
		}
		# when user presses Ctrl-C, finally blocks run, but catch blocks don't (imo, that's a weird design decision)
		# however, we need to cleanup the file in case we are interrupted by Ctrl-C, or iwr fails in another way
		#  (one would expect it to cleanup after itself like Start-BitsTransfer does, but apparently it doesn't, sigh)
		# we use a boolean flag $IwrFinished to basically recreate a catch block that catches even Ctrl-C
		$IwrFinished = $false
		try {
			Invoke-WebRequest $SrcUrl -OutFile $TargetPath @Params
			$IwrFinished = $true
		} finally {
			if (-not $IwrFinished) {
				rm -Force -LiteralPath $TargetPath -ErrorAction Ignore
			}
		}
	}
	Write-Debug "File downloaded."
	return Get-Item $TargetPath
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

function GetDownloadCacheEntry($Hash) {
	# each cache entry is a directory named with the SHA256 hash of target file,
	#  containing a single file with original name
	#  e.g. $script:DOWNLOAD_CACHE_DIR/<sha-256>/app-v1.2.0.zip

	$DirPath = Join-Path $script:DOWNLOAD_CACHE_DIR $Hash
	if (-not (Test-Path $DirPath)) {
		return $null
	}
	Write-Debug "Found matching download cache entry."

	# cache hit, validate file count
	$File = ls -File $DirPath
	if (@($File).Count -ne 1) {
		Write-Warning "Invalid download cache entry - contains multiple, or no items, erasing...: $Hash"
		rm -Recurse -LiteralPath $DirPath
		return $null
	}

	Write-Debug "Validating cache entry hash..."
	# validate file hash (to prevent tampering / accidental file corruption)
	$FileHash = GetFileHashWithProgressBar $File
	if ($Hash -ne $FileHash) {
		Write-Warning "Invalid download cache entry - content hash does not match, erasing...: $Hash"
		rm -Recurse -LiteralPath $DirPath
		return $null
	}
	Write-Debug "Cache entry hash validated."

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

	# if this is changed, also modify MoveFileToCache
	$DirPath = Join-Path $script:DOWNLOAD_CACHE_DIR $ExpectedHash.ToUpper()
	if (Test-Path $DirPath) {
		throw "Download cache already contains an entry for '$ExpectedHash' (from '$SrcUrl')."
	}

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
		# not -ErrorAction Ignore, we want to have a log in $Error for debugging
		rm -Recurse -Force -LiteralPath $DirPath -ErrorAction SilentlyContinue
		throw
	}
}

<# Assumes the hash is correct. #>
function MoveFileToCache($File, $Hash) {
	$Hash = $Hash.ToUpper()
	$DirPath = Join-Path $script:DOWNLOAD_CACHE_DIR $Hash
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
		$TmpDirPath = Join-Path $script:DOWNLOAD_TMP_DIR (New-Guid).Guid
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
