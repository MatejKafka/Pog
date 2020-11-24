. $PSScriptRoot\..\header.ps1
Import-Module $PSScriptRoot\..\Paths
Import-Module $PSScriptRoot\Common

# allows expanding .zip
Import-Module Microsoft.PowerShell.Archive
# allows downloading files using BITS
Import-Module BitsTransfer


function ExtractArchive($ArchiveFile, $TargetPath, [switch]$Force7zip) {
	if ($Force7zip -or $ArchiveFile.Name.EndsWith(".7z") -or $ArchiveFile.Name.EndsWith(".7z.exe")) {
		Write-Verbose "Expanding archive using 7zip..."
		# run 7zip with silenced status reports
		& $PSScriptRoot\bin\7za.exe x $ArchiveFile ("-o" + $TargetPath) -bso0 -bsp0
		if ($LastExitCode -gt 0) {
			throw "Could not expand archive: 7za.exe returned exit code $LastExitCode. There is likely additional output above."
		}
	} else {
		Write-Verbose "Expanding archive..."
		# Expand-Archive is really chatty with Verbose output, so we'll suppress it
		#  however, passing -Verbose:$false causes an erroneous verbose print to appear
		#  see: https://github.com/PowerShell/PowerShell/issues/14245
		try {
			$CurrentVerbose = $global:VerbosePreference
			$global:VerbosePreference = "SilentlyContinue"
			Expand-Archive -Path $ArchiveFile -DestinationPath $TargetPath -Force
		} finally {
			$global:VerbosePreference = $CurrentVerbose
		}
		
	}
}

Export function Install-FromUrl {
	[CmdletBinding(PositionalBinding=$false)]
	param(
			[Parameter(Mandatory, Position=0)]
			[string]
		$SrcUrl,
			# SHA256 hash that the downloaded archive should match
			# validation is skipped if null
			[string]
		$ExpectedHash,
			# normally, an archive root should contain a folder which contains the files;
			#  however, sometimes, the files are directly in the root of the zip archive; use this switch for these occasions
			[switch]
		$NoSubdirectory,
			# force the cmdlet to use 7za.exe binary to extract the archive
			# if not set, 7za.exe will be used for .7z and .7z.exe archives, and builtin Expand-Archive cmdlet for others
			[switch]
		$Force7zip
	)
	
	$TMP_EXPAND_PATH = ".\.install_tmp"
	
	if (Test-Path .\app) {
		$ErrorMsg = "Package is already installed - ./app subdirectory exists; pass -AllowOverwrite to overwrite it."
		$ShouldContinue = ConfirmOverwrite "Overwrite existing package installation?" `
			("Package seems to be already installed. Do you want to overwrite " +`
				"current installation (./app subdirectory)?`n" +`
				"Configuration and other package data will be kept.") `
			$ErrorMsg
	
		if (-not $ShouldContinue) {
			throw $ErrorMsg
		}
	
		Write-Verbose "Removing previous ./app directory..."
		rm -Recurse -Force .\app
	}
	
	if (Test-Path $TMP_EXPAND_PATH) {
		Write-Warning "Clearing orphaned tmp installer directory, probably from failed previous install..."
		rm -Recurse -Force $TMP_EXPAND_PATH
	}
	
	Write-Verbose "Downloading archive from '$SrcUrl'..."
	if (-not [string]::IsNullOrEmpty($ExpectedHash)) {
		# we have fixed hash, we can use download cache
		$DownloadedFile = Invoke-CachedFileDownload $SrcUrl -ExpectedHash $ExpectedHash
		ExtractArchive $DownloadedFile $TMP_EXPAND_PATH -Force7zip:$Force7zip
	} else {
		# the hash is not set, cannot safely cache the file
		$DownloadedFile = Invoke-TmpFileDownload $SrcUrl
		try {
			ExtractArchive $DownloadedFile $TMP_EXPAND_PATH -Force7zip:$Force7zip
		} finally {
			# remove the file after we finish
			rm $DownloadedFile
		}
	}
	
	if ($NoSubdirectory) {
		# use files from the root of archive directly
		Rename-Item $TMP_EXPAND_PATH .\app
	} else {
		try {
			$DirContent = ls $TMP_EXPAND_PATH
			# there should be a single folder here
			if (@($DirContent).Count -ne 1 -or -not $DirContent[0].PSIsContainer) {
				throw "There are multiple files in the root of the extracted archive, single directory expected. " +
					"Package author should pass '-NoSubdirectory' to 'Install-FromUrl' if the archive does not " +
					"have a wrapper directory in its root."
			}
			mv $DirContent .\app
		} finally {
			rm -Recurse $TMP_EXPAND_PATH
		}
	}
	Write-Verbose "Package successfully installed from downloaded archive."
}


function DownloadFile {
	param($SrcUrl, $TargetPath)
	
	# Unfortunately, BITS breaks for github and other pages with redirects, see here for description:
	#  https://powershell.org/forums/topic/bits-transfer-with-github/
	#$Description = "Downloading file from '$SrcUrl' to '$TargetPath'..."
	#Start-BitsTransfer $SrcUrl -Destination $TargetPath -Priority $global:Pkg_DownloadPriority -Description $Description
	
	if ($global:Pkg_DownloadPriority -ne "Foreground") {
		# TODO: FIXME
		Write-Warning "Low priority download requested by user (-LowPriority flag)."
		Write-Warning "Unfortunately, low priority downloads using BITS are currently disabled due to incompatibility with GitHub releases."
		Write-Warning " (see here for details: https://powershell.org/forums/topic/bits-transfer-with-github/)"
	}
	
	# we'll have to use Invoke-WebRequest instead, which doesn't offer low priority mode and has worse progress indicator
	Invoke-WebRequest $SrcUrl -OutFile $TargetPath
}


function GetDownloadCacheEntry($Hash) {
	$DirPath = Join-Path $script:DOWNLOAD_CACHE_DIR $Hash
	if (-not (Test-Path $DirPath)) {
		return $null
	}
	
	# cache hit, validate file count
	$File = ls -File $DirPath
	if (@($File).Count -ne 1) {
		Write-Warning "Invalid download cache entry - contains multiple, or no items, erasing...: $Hash"
		rm -Recurse $DirPath
		return $null
	}
	
	# validate file hash
	$FileHash = (Get-FileHash $File -Algorithm SHA256).Hash
	if ($Hash -ne $FileHash) {
		Write-Warning "Invalid download cache entry - content hash does not match, erasing...: $Hash"
		rm -Recurse $DirPath
		return $null
	}
	
	return $File
}

function Invoke-CachedFileDownload {
	param(
			[Parameter(Mandatory)]
			[string]
		$SrcUrl,
			[Parameter(Mandatory)]
			[string]
		$ExpectedHash # SHA256 hash of expected file
	)
	
	# each cache entry is a directory named with the SHA256 hash of target file,
	#  containing a single file with original name
	#  e.g. $script:DOWNLOAD_CACHE_DIR/<sha-256>/app-v1.2.0.zip
	
	$CachedFile = GetDownloadCacheEntry $ExpectedHash
	if ($null -ne $CachedFile) {
		Write-Verbose "Found cached copy of requested file."
		return $CachedFile
	}
	
	# cache miss, download the file
	Write-Verbose "Cached copy not found, downloading file to cache..."
	
	$DirPath = Join-Path $script:DOWNLOAD_CACHE_DIR $ExpectedHash
	# use file name from the URL
	$TargetPath = Join-Path $DirPath ([uri]$SrcUrl).Segments[-1]
	
	try {
		New-Item -Type Directory $DirPath
		DownloadFile $SrcUrl $TargetPath
		$File = Get-Item $TargetPath
		
		$RealHash = (Get-FileHash $File -Algorithm SHA256).Hash
		if ($ExpectedHash -ne $RealHash) {
			throw "Incorrect hash for file downloaded from $SrcUrl (expected : $ExpectedHash, real: $RealHash)."
		}
		# hash check passed, return file reference
		return $File
	} catch {
		# not -ErrorAction Ignore, we want to have a log in $Error for debugging
		rm -Recurse -Force $DirPath -ErrorAction SilentlyContinue 
		throw $_
	}
}


function Invoke-TmpFileDownload {
	param(
			[Parameter(Mandatory)]
			[string]
		$SrcUrl,
			[string]
		$ExpectedHash # SHA256 hash of expected file
	)
	
	# generate unused file name
	while ($true) {
		try {
			# use file name from the URL
			$TmpFileName = (New-Guid).Guid + "-" + ([uri]$SrcUrl).Segments[-1]
			$TmpPath = Join-Path ([System.IO.Path]::GetTempPath()) $TmpFileName
			$TmpFile = New-item $TmpPath
			break
		} catch {}
	}
	
	# we have a temp file with unique name and requested extension, download content
	DownloadFile $SrcUrl $TmpFile
	
	if (-not [string]::IsNullOrEmpty($ExpectedHash)) {
		$RealHash = (Get-FileHash $TmpFile -Algorithm SHA256).Hash
		if ($ExpectedHash -ne $RealHash) {
			rm -Force $TmpFile -ErrorAction SilentlyContinue
			throw "Incorrect hash for file downloaded from $SrcUrl (expected : $ExpectedHash, real: $RealHash)."
		}
	}
	
	# hash check passed, return file reference
	return $TmpFile
}