. $PSScriptRoot\..\header.ps1
Import-Module $PSScriptRoot\..\Paths
Import-Module $PSScriptRoot\Confirmations

# allows expanding .zip
Import-Module Microsoft.PowerShell.Archive
# allows downloading files using BITS
Import-Module BitsTransfer


$7ZipCmd = Get-Command "7z" -ErrorAction Ignore
if ($null -eq $7ZipCmd) {
	throw "Could not find 7zip (command '7z'), which is used for package installation. " +`
			"It is supposed to be installed as a normal Pkg package, unless you manually removed it. " +`
			"If you know why this happened, please restore 7zip and run this again. " +`
			"If you don't, contact Pkg developers and we'll hopefully figure out where's the issue."
}

# TODO: create tar package for better compatibility
$TarCmd = Get-Command "tar" -ErrorAction Ignore
if ($null -eq $TarCmd) {
	throw "Could not find tar (command 'tar'), which is used for package installation. " +`
			"It is supposed to be installed systemwide in C:\Windows\System32\tar.exe since Windows 10 v17063. " +`
			"If you don't know why it's missing, either download it yourself and put it on PATH, " +`
			"or contact Pkg developers and we'll hopefully figure out where's the issue."	
}


function ExtractArchive($ArchiveFile, $TargetPath, [switch]$Force7zip) {
	Write-Debug "Expanding archive (name: '$($ArchiveFile.Name), target: $TargetPath)')."
	# only use Expand-Archive for .zip, 7zip for everything else
	if (-not $Force7zip -and $ArchiveFile.Name.EndsWith(".tar.gz")) {
		Write-Information "Expanding archive using 'tar'..."
		# tar expects the target dir to exist, so we'll create it
		$null = ni -Type Directory $TargetPath
		# run tar
		# -f <file> = expanded archive
		# -m = do not restore modification times
		# -C <dir> = dir to extract to
		& $TarCmd --extract -f $ArchiveFile -m -C $TargetPath
		if ($LastExitCode -gt 0) {
			throw "Could not expand archive: 7zip returned exit code $LastExitCode. There is likely additional output above."
		}
		if (-not (Test-Path $TargetPath)) {
			throw "'tar' indicated success, but the extracted directory is not present. " +`
					"Seems like Pkg developers fucked something up, plz send bug report."
		}
	} elseif ($Force7zip -or -not $ArchiveFile.Name.EndsWith(".zip")) {
		Write-Information "Expanding archive using '7zip'..."
		# run 7zip
		# bso0 = disable output
		# bsp0 = disable progress reports
		# bse1 = send errors to stdout
		# aoa = automatically overwrite existing files
		#  (should not usually occur, unless the archive is a bit malformed,
		#  but e.g. wireshark NSIS installer does it for some reason)
		& $7ZipCmd x $ArchiveFile ("-o" + $TargetPath) -bso0 -bsp0 -bse1 -aoa
		if ($LastExitCode -gt 0) {
			throw "Could not expand archive: 7zip returned exit code $LastExitCode. There is likely additional output above."
		}
		if (-not (Test-Path $TargetPath)) {
			throw "'7zip' indicated success, but the extracted directory is not present. " +`
					"Seems like Pkg developers fucked something up, plz send bug report."
		}
	} else {
		Write-Information "Expanding archive..."
		# Expand-Archive is really chatty with Verbose output, so we'll suppress it
		#  however, passing -Verbose:$false causes an erroneous verbose print to appear
		#  see: https://github.com/PowerShell/PowerShell/issues/14245
		try {
			$CurrentVerbose = $global:VerbosePreference
			$global:VerbosePreference = "SilentlyContinue"
			$null = Expand-Archive -Path $ArchiveFile -DestinationPath $TargetPath -Force
		} catch [IO.FileFormatException] {
			throw "Could not expand archive: File format not recognized by Expand-Archive. " +`
					"For manifest authors: If the format is something 7zip should recognize, " +`
					"pass '-Force7zip' switch to 'Install-FromUrl' in package manifest, " +`
					"or change the URL file extension to '.7z'."
		} catch {
			throw "Could not expand archive: $_"
		} finally {
			$global:VerbosePreference = $CurrentVerbose
		}
	}
	Write-Debug "Archive expanded to '$TargetPath'."
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
		
		# do not remove the ./app directory just yet
		# first, we'll download the new version, and after
		#  all checks pass and we know we managed to set it up correctly,
		#  we'll delete the old version
	}
	
	if (Test-Path $TMP_EXPAND_PATH) {
		Write-Warning "Clearing orphaned tmp installer directory, probably from failed previous install..."
		rm -Recurse -Force $TMP_EXPAND_PATH
	}
	
	Write-Information "Retrieving archive from '$SrcUrl'..."
	if (-not [string]::IsNullOrEmpty($ExpectedHash)) {
		# we have fixed hash, we can use download cache
		$DownloadedFile = Invoke-CachedFileDownload $SrcUrl -ExpectedHash $ExpectedHash.ToUpper()
		Write-Debug "File correctly retrieved, expanding to '$TMP_EXPAND_PATH'..."
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
		if (Test-Path .\app) {
			Write-Information "Removing previous ./app directory..."
			rm -Recurse -Force .\app
		}
	
		# use files from the root of archive directly
		Write-Debug "Renaming '$TMP_EXPAND_PATH' to './app'..."
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
			
			if (Test-Path .\app) {
				Write-Information "Removing previous ./app directory..."
				rm -Recurse -Force .\app
			}
			
			Write-Debug "Moving extracted directory to './app'..."			
			mv $DirContent .\app
		} finally {
			rm -Recurse $TMP_EXPAND_PATH
		}
	}
	Write-Information "Package successfully installed from downloaded archive."
}


function DownloadFile {
	param($SrcUrl, $TargetPath)
	
	Write-Debug "Downloading file from '$SrcUrl' to '$TargetPath'."
	
	# Unfortunately, BITS breaks for github and other pages with redirects, see here for description:
	#  https://powershell.org/forums/topic/bits-transfer-with-github/
	#$Description = "Downloading file from '$SrcUrl' to '$TargetPath'..."
	#Start-BitsTransfer $SrcUrl -Destination $TargetPath -Priority $global:Pkg_DownloadPriority -Description $Description
	
	if ($global:Pkg_DownloadPriority -ne "Foreground") {
		# TODO: first resolve all redirects, then download using BITS
		Write-Warning "Low priority download requested by user (-LowPriority flag)."
		Write-Warning "Unfortunately, low priority downloads using BITS are currently disabled due to incompatibility with GitHub releases."
		Write-Warning " (see here for details: https://powershell.org/forums/topic/bits-transfer-with-github/)"
	}
	
	# we'll have to use Invoke-WebRequest instead, which doesn't offer low priority mode and has worse progress indicator
	Invoke-WebRequest $SrcUrl -OutFile $TargetPath
	Write-Debug "File downloaded."
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
		rm -Recurse $DirPath
		return $null
	}
	
	Write-Debug "Validating cache entry hash..."
	# validate file hash (to prevent tampering / accidental file corruption)
	$FileHash = (Get-FileHash $File -Algorithm SHA256).Hash
	if ($Hash -ne $FileHash) {
		Write-Warning "Invalid download cache entry - content hash does not match, erasing...: $Hash"
		rm -Recurse $DirPath
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
			# SHA256 hash of expected file
			[Parameter(Mandatory)]
			[string]
		$ExpectedHash	
	)
	
	$DirPath = Join-Path $script:DOWNLOAD_CACHE_DIR $ExpectedHash.ToUpper()
	if (Test-Path $DirPath) {
		throw "Download cache already contains an entry for '$ExpectedHash' (from '$SrcUrl')."
	}
	
	# use file name from the URL
	$TargetPath = Join-Path $DirPath ([uri]$SrcUrl).Segments[-1]
	Write-Debug "Target download path: '$TargetPath'."
	
	try {
		$null = New-Item -Type Directory $DirPath
		Write-Debug "Created download cache dir '$ExpectedHash'."
		DownloadFile $SrcUrl $TargetPath
		$File = Get-Item $TargetPath
		
		$RealHash = (Get-FileHash $File -Algorithm SHA256).Hash
		if ($ExpectedHash -ne $RealHash) {
			throw "Incorrect hash for file downloaded from $SrcUrl (expected : $ExpectedHash, real: $RealHash)."
		}
		Write-Debug "Hash check passed, file was correctly downloaded to cache."
		# hash check passed, return file reference
		return $File
	} catch {
		# not -ErrorAction Ignore, we want to have a log in $Error for debugging
		rm -Recurse -Force $DirPath -ErrorAction SilentlyContinue 
		throw $_
	}
}

function Invoke-CachedFileDownload {
	param(
			[Parameter(Mandatory)]
			[string]
		$SrcUrl,
			# SHA256 hash of expected file
			[Parameter(Mandatory)]
			[string]
		$ExpectedHash
	)
	
	Write-Debug "Checking if we have a cached copy for '$ExpectedHash'..."
	$CachedFile = GetDownloadCacheEntry $ExpectedHash
	if ($null -ne $CachedFile) {
		Write-Verbose "Found cached copy of requested file."
		return $CachedFile
	}
	
	Write-Verbose "Cached copy not found, downloading file to cache..."
	return DownloadFileToCache $SrcUrl $ExpectedHash
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