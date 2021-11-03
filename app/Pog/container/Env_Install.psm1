# Requires -Version 7
. $PSScriptRoot\..\lib\header.ps1
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

$TMP_EXPAND_PATH = ".\.install_tmp"

$7ZipCmd = Get-Command "7z" -ErrorAction Ignore
if ($null -eq $7ZipCmd) {
	throw "Could not find 7zip (command '7z'), which is used for package installation. " +`
			"It is supposed to be installed as a normal Pog package, unless you manually removed it. " +`
			"If you know why this happened, please restore 7zip and run this again. " +`
			"If you don't, contact Pog developers and we'll hopefully figure out where's the issue."
}

# TODO: create tar package for better compatibility
$TarCmd = Get-Command "tar" -ErrorAction Ignore
if ($null -eq $TarCmd) {
	throw "Could not find tar (command 'tar'), which is used for package installation. " +`
			"It is supposed to be installed systemwide in C:\Windows\System32\tar.exe since Windows 10 v17063. " +`
			"If you don't know why it's missing, either download it yourself and put it on PATH, " +`
			"or contact Pog developers and we'll hopefully figure out where's the issue."
}


<# This function is called after the Install script finishes. #>
Export function __cleanup {
	# nothing for now
}

<# This function is called after the container setup is finished to run the passed script. #>
Export function __main {
	param($Installer, $PackageArguments)

	if ($Installer -is [scriptblock]) {
		& $Installer @PackageArguments
	} else {
		if ($Installer.Url -is [scriptblock]) {
			$Installer.Url = & $Installer.Url
		}
		# Install block is a hashtable of arguments to Install-FromUrl
		Install-FromUrl @Installer
	}
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

	if ($LastExitCode -gt 0) {
		throw "Could not expand archive: 7zip returned exit code $LastExitCode. There is likely additional output above."
	}
	if (-not (Test-Path $TargetPath)) {
		throw "'7zip' indicated success, but the extracted directory is missing. " +`
				"Seems like Pog developers fucked something up, plz send bug report."
	}
}

function ExtractArchive($ArchiveFile, $TargetPath, [switch]$Force7zip) {
	# see last comment in DownloadFile for explanation of this weird try/finally construct
	$ExtractionFinished = $false
	try {
		_ExtractArchive_Inner $ArchiveFile $TargetPath -Force7zip:$Force7zip
		$ExtractionFinished = $true
	} finally {
		if (-not $ExtractionFinished) {
			rm -Recurse -Force $TargetPath -ErrorAction Ignore
		}
	}
}

function _ExtractArchive_Inner($ArchiveFile, $TargetPath, [switch]$Force7zip) {
	Write-Debug "Expanding archive (name: '$($ArchiveFile.Name), target: $TargetPath)')."
	# use Expand-Archive for .zip, tar for .tar.gz and 7zip for everything else
	# tar is used for .tar.gz, because 7zip doesn't extract it in one step (it goes .tar.gz -> .tar -> content instead)
	if (-not $Force7zip -and $ArchiveFile.Name.EndsWith(".tar.gz")) {
		Write-Information "Expanding archive using 'tar'..."
		# tar expects the target dir to exist, so we'll create it
		$null = New-Item -Type Directory $TargetPath
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
					"Seems like Pog developers fucked something up, plz send bug report."
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

function Get-ExtractedDirPath([string]$Subdirectory) {
	$DirContent = ls $TMP_EXPAND_PATH
	if (-not $Subdirectory) {
		if (@($DirContent).Count -eq 1 -and $DirContent[0].PSIsContainer) {
			# single directory in archive root (as is common for Linux-style archives)
			Write-Debug "Archive root contains single directory '$DirContent', using it for './app'."
			return $DirContent[0]
		} else {
			# no single subdirectory, multiple files in root (Windows-style archive)
			Write-Debug "Archive root contains multiple items, using archive root directly for './app'."
			return $TMP_EXPAND_PATH
		}
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
		return Get-Item $DirPath
	}
}

Export function Install-FromUrl {
	[CmdletBinding(PositionalBinding=$false)]
	param(
			[Parameter(Mandatory, Position=0)]
			[string]
			[Alias("Url")]
		$SrcUrl,
			<#
			SHA256 hash that the downloaded archive should match.
			Validation is skipped if null, but warning is printed

			If '?' is passed, nothing will be installed, we will download the file, compute SHA-256 hash and print it out.
			This is intended to be used when writing a new manifest and trying to figure out the hash of the file.
			#>
			[string]
			[Alias("Hash")]
			[ValidateScript({
				if ($_ -ne "?" -and $_ -notmatch '^(\-|[a-fA-F0-9]{64})$') {
					throw "Parameter must be hex string (SHA-256 hash), 64 characters long (or '?'), got '$_'."
				}
				return $true
			})]
		$ExpectedHash,
			# if passed, only the subdirectory with passed name/path is extracted to ./app
			#  and the rest is ignored
			[string]
		$Subdirectory = "",
			# force the cmdlet to use 7za.exe binary to extract the archive
			# if not set, 7z.exe will be used for .7z and .7z.exe archives, and builtin Expand-Archive cmdlet for others
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
		Write-Debug "Passed '-NsisInstaller', automatically applying `-Force7zip`."
		$Force7zip = $true
	}

	$DownloadParams = @{
		UserAgent = $UserAgent
	}

	if (Test-Path $TMP_EXPAND_PATH) {
		Write-Warning "Clearing orphaned tmp installer directory, probably from failed previous install..."
		rm -Recurse -Force $TMP_EXPAND_PATH
	}

	# do not continue installation if manifest writer just wants to get the file hash
	if ($ExpectedHash -eq "?") {
		Write-Host ""
		Write-Host "    NOTE: Not installing, only retrieving the file hash." -ForegroundColor Magenta
		$Hash = GetUrlFileHash $SrcUrl -DownloadParams $DownloadParams -ShouldCache
		Write-Host ""
		Write-Host "    Hash for the file at '$SrcUrl':" -ForegroundColor Magenta
		Write-Host "    $Hash" -ForegroundColor Magenta
		Write-Host ""
		# it seems more ergonomic to exit than return
		exit
	}

	if (Test-Path .\app) {
		# first, we check if we can move/delete the ./app directory
		# e.g. maybe the packaged program is running and holding a lock over a file inside
		# if that would be the case, we would extract the package and then get
		#  Acess Denied error, and user would waste his time waiting for the extraction all over again
		Write-Debug "Checking if we can manipulate the existing './app' directory..."
		try {
			# it seems there isn't a more direct way to check if we can delete the directory
			Move-Item .\app .\_app
		} catch {
			# TODO: use OpenFilesView to list the open file handles and programs
			# FIXME: apparently, this may end up in a half-moved state where both app and _app exist
			#  that are preventing us from overwriting the .\app directory
			throw "There is an existing package installation, which we cannot overwrite (getting 'Access Denied') - " +`
				"is it possible that some program from the package is running, " +`
				"or that another program is using a file from this package?"
		}

		try {
			Move-Item .\_app .\app
		} catch {
			# ok, seriously, wtf?
			throw "Cannot move './app' directory back into place. Something seriously broken just happened." +`
				"Seems like Pog developers fucked something up, plz send bug report."
		}

		$ShouldContinue = ConfirmOverwrite "Overwrite existing package installation?" `
			("Package seems to be already installed. Do you want to overwrite " +`
				"current installation (./app subdirectory)?`n" +`
				"Configuration and other package data will be kept.")

		if (-not $ShouldContinue) {
			Write-Information "Not installing, user refused to overwrite existing package installation."
			return
		}

		# do not remove the ./app directory just yet; first, we'll download the new version,
		#  and after all checks pass and we know we managed to set it up correctly,
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
				"but it's better style to include a checksum, and improves security and reproducibility.")
		# the hash is not set, cannot safely cache the file
		$TmpDir, $DownloadedFile = Invoke-TmpFileDownload $SrcUrl -DownloadParams $DownloadParams
		try {
			ExtractArchive $DownloadedFile $TMP_EXPAND_PATH -Force7zip:$Force7zip
		} finally {
			# remove the temporary dir (including the file) after we finish
			rm -Recurse $TmpDir
			Write-Debug "Removed temporary downloaded archive '$DownloadedFile'."
		}
	}

	try {
		$ExtractedDir = Get-ExtractedDirPath -Subdirectory $Subdirectory

		if (Test-Path .\app) {
			Write-Information "Removing previous ./app directory..."
			rm -Recurse -Force .\app
		}

		Write-Debug "Moving extracted directory '$ExtractedDir' to './app'..."
		Move-Item -LiteralPath $ExtractedDir .\app

	} finally {
		rm -Recurse -Force -LiteralPath $TMP_EXPAND_PATH -ErrorAction Ignore
	}

	if ($NsisInstaller) {
		if (-not (Test-Path -Type Container ./app/`$PLUGINSDIR)) {
			throw "'-NsisInstaller' flag was passed to 'Install-FromUrl' in package manifest, " + `
				"but directory `$PLUGINSDIR does not exist in the extracted path (NSIS self-extracting archive should contain it)."
		}
		rm -Recurse ./app/`$PLUGINSDIR
		Write-Debug "Removed `$PLUGINSDIR directory from extracted NSIS installer archive."
	}

	Write-Information "Package successfully installed from downloaded archive."
}


function DownloadFile {
	param($SrcUrl, $TargetDir, [UserAgentType]$UserAgent)

	Write-Debug "Downloading file from '$SrcUrl' to directory '$TargetDir'."

	# in case other parameters are added, figure out if they can be passed to Start-BitsTransfer, or just iwr
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

function Invoke-CachedFileDownload {
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

function GetUrlFileHash($SrcUrl, $DownloadParams, [switch]$ShouldCache) {
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


function Invoke-TmpFileDownload {
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