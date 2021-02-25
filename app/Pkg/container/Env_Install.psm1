. $PSScriptRoot\..\header.ps1
Import-Module $PSScriptRoot\..\Paths
Import-Module $PSScriptRoot\Confirmations

# allows expanding .zip
Import-Module Microsoft.PowerShell.Archive
# allows downloading files using BITS
Import-Module BitsTransfer


Export-ModuleMember -Function Confirm-Action


enum UserAgentType {
	PowerShell
	Browser
	Wget
}


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


function ExtractArchive7Zip($ArchiveFile, $TargetPath) {
	$ArchiveName = Split-Path -Leaf $ArchiveFile

	function ShowProgress([int]$Percentage) {
		Write-Progress `
			-Activity "Extracting package with 7zip" `
			-Status "Extracting package from '$ArchiveName'..." `
			-PercentComplete $Percentage `
			-Completed:($Percentage -eq 100)
	}
	
	# if these seem a bit cryptic to you, you are a sane human being, congratulations
	$Params = @(
		"-bso0" # disable normal output
		"-bsp1" # disable progress reports
		"-bse1" # send errors to stdout
		"-aoa" # automatically overwrite existing files
			# (should not usually occur, unless the archive is a bit malformed,
			# but NSIS installers occasionally do it for some reason)
	)
	
	ShowProgress 0
	
	# run 7zip
	& $7ZipCmd x $ArchiveFile ("-o" + $TargetPath) @Params | % {
		# progress print pattern
		# e.g. ' 34% 10 - glib-2.dll'
		$Pattern = [regex]"\s*(\d{1,3})%.*"
		if ($_ -match $Pattern) {
			ShowProgress ([int]$Pattern.Match($_).Groups[1].Value)
		} elseif ($_.Trim().StartsWith("0M Scan ")) {
			# ignore this initial line
		} elseif (-not [string]::IsNullOrWhiteSpace($_)) {
			echo $_
		}
	}
	
	# hide progress bar
	ShowProgress 100
	sleep 2
	
	if ($LastExitCode -gt 0) {
		throw "Could not expand archive: 7zip returned exit code $LastExitCode. There is likely additional output above."
	}
	
	if (-not (Test-Path $TargetPath)) {
		throw "'7zip' indicated success, but the extracted directory is not present. " +`
				"Seems like Pkg developers fucked something up, plz send bug report."
	}
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
		ExtractArchive7Zip $ArchiveFile $TargetPath
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
			# validation is skipped if null, but warning is printed
			#
			# if '?' is passed, nothing will be installed, Pkg will download the file, compute SHA-256 hash and print it out
			# this is intended to be used when writing a new manifest and trying to figure out the hash of the file
			[string]
			[ValidateScript({
				if ($_ -ne "?" -and $_ -notmatch '^(\-|[a-zA-Z0-9]*)$') {
					throw "Parameter must be alphanumeric string, 64 characters long (or '?'), got '$_'."
				}
				return $true
			})]
		$ExpectedHash,
			# if passed, only the subdirectory with passed name/path is extracted to ./app
			#  and the rest is ignored
			[string]
		$Subdirectory = "",
			# normally, an archive root should contain a folder which contains the files;
			#  however, sometimes, the files are directly in the root of the zip archive; use this switch for these occasions
			[switch]
		$NoSubdirectory,
			# force the cmdlet to use 7za.exe binary to extract the archive
			# if not set, 7za.exe will be used for .7z and .7z.exe archives, and builtin Expand-Archive cmdlet for others
			[switch]
		$Force7zip,
			# some servers (e.g. Apache Lounge) dislike PowerShell user agent string for some reason
			# set this to `Browser` to use a browser user agent string (currently Firefox)
			# set this to `Wget` to use wget user agent string
			[UserAgentType]
		$UserAgent = [UserAgentType]::PowerShell,
			# pass this if the file is an NSIS installer
			# currently, only thing this does is remove the `$PLUGINSDIR` output directory
			[switch]
		$NsisInstaller
	)
	
	
	if ($NsisInstaller) {
		Write-Debug "Passed '-NsisInstaller', automatically applying `-Force7zip` and `-NoSubdirectory`."
		$Force7zip = $true
		# TODO: is it not possible that someone has an NSIS installer, but only needs one subdirectory?
		#  (this would then collide with `$Subdirectory` param)
		$NoSubdirectory = $true
	}
	
	if ($NoSubdirectory -and -not [string]::IsNullOrEmpty($Subdirectory)) {
		throw "'-NoSubdirectory' switch must not be passed together with '-Subdirectory <path>'."
	}
	
	$DownloadParams = @{
		UserAgent = $UserAgent
	}
	
	
	$TMP_EXPAND_PATH = ".\.install_tmp"
	
	if (Test-Path $TMP_EXPAND_PATH) {
		Write-Warning "Clearing orphaned tmp installer directory, probably from failed previous install..."
		rm -Recurse -Force $TMP_EXPAND_PATH
	}
	
	# do not continue installation if manifest writer just wants to get the file hash
	if ($ExpectedHash -eq "?") {
		Write-Host ""
		Write-Host "    NOTE: Not installing, only retrieving the file hash." -ForegroundColor Magenta
		$Hash = GetUrlFileHash $SrcUrl -DownloadParams $DownloadParams
		Write-Host ""
		Write-Host "    Hash for the file at '$SrcUrl':" -ForegroundColor Magenta
		Write-Host ("    " + $Hash.Hash) -ForegroundColor Magenta
		Write-Host ""
		return
	}
	
	if (Test-Path .\app) {
		$ShouldContinue = ConfirmOverwrite "Overwrite existing package installation?" `
			("Package seems to be already installed. Do you want to overwrite " +`
				"current installation (./app subdirectory)?`n" +`
				"Configuration and other package data will be kept.")
	
		if (-not $ShouldContinue) {
			throw $ErrorMsg
		}
		
		# do not remove the ./app directory just yet
		# first, we'll download the new version, and after
		#  all checks pass and we know we managed to set it up correctly,
		#  we'll delete the old version
	}
	
	
	Write-Information "Retrieving archive from '$SrcUrl' (or local cache)..."
	if (-not [string]::IsNullOrEmpty($ExpectedHash)) {
		# we have fixed hash, we can use download cache
		$DownloadedFile = Invoke-CachedFileDownload $SrcUrl `
				-ExpectedHash $ExpectedHash.ToUpper() -DownloadParams $DownloadParams
		Write-Debug "File correctly retrieved, expanding to '$TMP_EXPAND_PATH'..."
		ExtractArchive $DownloadedFile $TMP_EXPAND_PATH -Force7zip:$Force7zip
	} else {
		Write-Warning ("Downloading a file from '${SrcUrl}', but no checksum was provided in the package " +`
				"(passed to 'Install-FromUrl'). This means that we cannot be sure if the download file is the " +`
				"same one package author intended. This may or may not be a problem on its own, " +`
				"but it's better style to include a checksum.")
		# the hash is not set, cannot safely cache the file
		$DownloadedFile = Invoke-TmpFileDownload $SrcUrl -DownloadParams $DownloadParams
		try {
			ExtractArchive $DownloadedFile $TMP_EXPAND_PATH -Force7zip:$Force7zip
		} finally {
			# remove the file after we finish
			rm -Force $DownloadedFile
			Write-Debug "Removed downloaded archive from TMP."
		}
	}
	
	if ($NoSubdirectory) {
		if (Test-Path .\app) {
			Write-Information "Removing previous ./app directory..."
			rm -Recurse -Force .\app
		}

		$DirContent = ls $TMP_EXPAND_PATH
		if (@($DirContent).Count -eq 1 -and $DirContent[0].PSIsContainer) {
			Write-Warning ("-NoSubdirectory was passed to Install-FromUrl, but the archive " +`
					"contains only a single directory, so it doesn't really make sense to pass the switch.")
		}
	
		# use files from the root of archive directly
		Write-Debug "Renaming '$TMP_EXPAND_PATH' to './app'..."
		Rename-Item $TMP_EXPAND_PATH .\app
	} else {
		try {
			$Src = if ([string]::IsNullOrEmpty($Subdirectory)) {
				$DirContent = ls $TMP_EXPAND_PATH
				# there should be a single folder here
				if (@($DirContent).Count -ne 1 -or -not $DirContent[0].PSIsContainer) {
					$Dirs = ($DirContent.Name | % {"'" + $_ + "'"}) -join ", "
					throw ("There are multiple files in the root of the extracted archive, single directory expected. " +`
						"Package author should pass '-NoSubdirectory' to 'Install-FromUrl' if the archive does not " +`
						"have a wrapper directory in its root. " +`
						"Root of the archive contains the following items: $Dirs.")
				}
				$DirContent

			} else {
				$DirPath = Join-Path $TMP_EXPAND_PATH $Subdirectory
				Write-Debug "Using passed path inside archive: '$DirPath'."
				
				# test if the path exists in the extracted directory
				if (-not (Test-Path -Type Container $DirPath)) {
					$Dirs = (ls $TMP_EXPAND_PATH).Name | % {"'" + $_ + "'"}
					$Dirs = $Dirs -join ", "
					throw "'-Subdirectory $Subdirectory' param was provided to 'Install-FromUrl' " +`
						"in package manifest, but the directory does not exist inside the archive. " +`
						"Root of the archive contains the following items: $Dirs."
				}				
				Get-Item $DirPath
			}
			
			if (Test-Path .\app) {
				Write-Information "Removing previous ./app directory..."
				rm -Recurse -Force .\app
			}
			
			Write-Debug "Moving extracted directory '$Src' to './app'..."			
			mv $Src ./app
			
		} finally {
			rm -Recurse $TMP_EXPAND_PATH
		}
	}
	
	if ($NsisInstaller) {
		if (-not (Test-Path -Type Container ./app/`$PLUGINSDIR)) {
			throw "'-NsisInstaller' flag was passed to 'Install-FromUrl' in package manifest, " +`
				"but directory $PLUGINSDIR does not exist in the extracted path (and it should for an NSIS installer)."
		}
		rm -Recurse ./app/`$PLUGINSDIR
		Write-Debug "Removed `$PLUGINSDIR directory from extracted NSIS installer archive."
	}
	
	Write-Information "Package successfully installed from downloaded archive."
}


function DownloadFile {
	param($SrcUrl, $TargetPath, [UserAgentType]$UserAgent)
	
	Write-Debug "Downloading file from '$SrcUrl' to '$TargetPath'."
	
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

	# TODO: this lets us figure out real URL, which could then be passed to Start-BitsTransfer
	#       however, Start-BitsTransfer cannot fake user agent, but maybe we could figure out a workaround
	#  [System.Net.HttpWebRequest]::Create('https://URL').GetResponse().ResponseUri.AbsoluteUri
	
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
	Invoke-WebRequest $SrcUrl -OutFile $TargetPath @Params
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
		$ExpectedHash,
			[Hashtable]
		$DownloadParams = {}
	)
	
	$DirPath = Join-Path $script:DOWNLOAD_CACHE_DIR $ExpectedHash.ToUpper()
	if (Test-Path $DirPath) {
		throw "Download cache already contains an entry for '$ExpectedHash' (from '$SrcUrl')."
	}
	
	# TODO: extract it from the HTTP request headers
	# use file name from the URL
	$TargetPath = Join-Path $DirPath ([uri]$SrcUrl).Segments[-1]
	Write-Debug "Target download path: '$TargetPath'."
	
	try {
		$null = New-Item -Type Directory $DirPath
		Write-Debug "Created download cache dir '$ExpectedHash'."
		DownloadFile $SrcUrl $TargetPath @DownloadParams
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


function GetUrlFileHash($SrcUrl, $DownloadParams) {
	$File = Invoke-TmpFileDownload $SrcUrl -DownloadParams $DownloadParams
	try {
		return Get-FileHash $File
	} finally {
		rm -Force $File
	}
}


function Invoke-TmpFileDownload {
	param(
			[Parameter(Mandatory)]
			[string]
		$SrcUrl,
			[Hashtable]
		$DownloadParams = @{}
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
	DownloadFile $SrcUrl $TmpFile @DownloadParams	
	return $TmpFile
}