# Requires -Version 7
. $PSScriptRoot\..\..\lib\header.ps1
Import-Module $PSScriptRoot\..\container_lib\Confirmations
Import-Module $PSScriptRoot\ExtractArchive
Import-Module $PSScriptRoot\FileDownloader

Export-ModuleMember -Function Confirm-Action


$TMP_EXPAND_PATH = ".\.install_tmp"


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
			# force the cmdlet to use 7z.exe binary to extract the archive
			# if not set, 7z.exe will be used for .7z and .7z.exe archives, and builtin Expand-Archive cmdlet for others
			[switch]
		$Force7zip,
			# some servers (e.g. Apache Lounge) dislike PowerShell user agent string for some reason
			# set this to `Browser` to use a browser user agent string (currently Firefox)
			# set this to `Wget` to use wget user agent string
			[UserAgentType]
		$UserAgent = [UserAgentType]::PowerShell,
			# pass this if the retrieved file is an NSIS installer
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
		$Hash = Get-UrlFileHash $SrcUrl -DownloadParams $DownloadParams -ShouldCache
		Write-Host ""
		Write-Host "    Hash for the file at '$SrcUrl' (copied to clipboard):" -ForegroundColor Magenta
		Write-Host "    $Hash" -ForegroundColor Magenta
		Write-Host ""
		$Hash | Set-Clipboard
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
