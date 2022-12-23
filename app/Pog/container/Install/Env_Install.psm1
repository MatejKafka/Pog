# Requires -Version 7
using module .\..\container_lib\Confirmations.psm1
using module .\LockedFiles.psm1
. $PSScriptRoot\..\..\lib\header.ps1


$TMP_EXPAND_PATH = ".\.install_tmp"



<# This function is called after the container setup is finished to run the passed manifest. #>
Export function __main {
	param($Manifest, $PackageArguments)

	if ($Manifest.Install -is [scriptblock]) {
		# run the installer scriptblock
		# see Env_Enable\__main for explanation of .GetNewClosure()
		& $Manifest.Install.GetNewClosure() @PackageArguments
	} else {
		# Install block is a hashtable of arguments to Install-FromUrl
		# create a copy, do not modify the main manifest
		$Installer = $Manifest.Install.Clone()

		# resolve SourceUrl/Url scriptblocks
		foreach ($Prop in @("SourceUrl", "Url")) {
			if ($Installer.ContainsKey($Prop) -and $Installer[$Prop] -is [scriptblock]) {
				# see Env_Enable\__main for explanation of .GetNewClosure()
				$Installer[$Prop] = & $Installer[$Prop].GetNewClosure()
			}
		}

		Install-FromUrl @Installer
	}
}

<# This function is called after __main finishes, even if it fails or gets interrupted. #>
Export function __cleanup {
	# nothing for now
}



<# Retrieves the requested archive/file (either downloading it, or using a cached version), and extracts it to $TMP_EXPAND_PATH.
   The extraction is not atomic – that is, if it fails or gets interrupted, $TMP_EXPAND_PATH may be left in a half-extracted state. #>
function RetrieveAndExpandArchive($SourceUrl, $ExpectedHash, $DownloadParams, $Package, [switch]$NoArchive) {
	$LockedFile = $null
	try {
		$LockedFile = Invoke-FileDownload $SourceUrl -ExpectedHash $ExpectedHash -DownloadParameters $DownloadParams -Package $Package
		Write-Debug "File correctly retrieved, expanding to '$TMP_EXPAND_PATH'..."

		if (-not $NoArchive) {
			Expand-Archive7Zip $LockedFile.Path $TMP_EXPAND_PATH
		} else {
			# FIXME: bring back the optimization where a temporary directory is moved instead of a copy
			$null = New-Item -Type Directory $TMP_EXPAND_PATH
			Copy-Item $LockedFile.Path $TMP_EXPAND_PATH
		}
	} finally {
		if ($null -ne $LockedFile) {$LockedFile.Dispose()}
	}
}

function Get-ExtractedAppDirectory([string]$Subdirectory) {
	$DirContent = ls $TMP_EXPAND_PATH
	if (-not $Subdirectory) {
		if (@($DirContent).Count -eq 1 -and $DirContent[0].PSIsContainer) {
			# single directory in archive root (as is common for Linux-style archives)
			Write-Debug "Archive root contains single directory '$DirContent', using it for './app'."
			return $DirContent[0]
		} else {
			# no single subdirectory, multiple files in root (Windows-style archive)
			Write-Debug "Archive root contains multiple items, using archive root directly for './app'."
			return Get-Item $TMP_EXPAND_PATH
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

function PrepareNewAppDirectory($SrcDirectory, [scriptblock]$SetupScript, [switch]$NsisInstaller) {
	if ($NsisInstaller) {
		$NsisPath = Join-Path $SrcDirectory '$PLUGINSDIR'
		if (-not (Test-Path -Type Container $NsisPath)) {
			throw "'-NsisInstaller' flag was passed to 'Install-FromUrl' in package manifest, " + `
				"but the directory '`$PLUGINSDIR' does not exist in the extracted path (NSIS self-extracting archive should contain it)."
		}
		rm -Recurse -Force $NsisPath
		Write-Debug "Removed `$PLUGINSDIR directory from the extracted NSIS installer archive."
	}
	if ($SetupScript) {
		# run the -SetupScript with a changed directory
		$OrigWD = Get-Location
		try {
			Set-Location $SrcDirectory
			& $SetupScript
		} finally {
			Set-Location $OrigWD
		}
	}
}

Export function Install-FromUrl {
	[CmdletBinding(PositionalBinding=$false)]
	param(
			### Source URL, from which the archive is downloaded. Redirects are supported.
			[Parameter(Mandatory, Position=0)]
			[string]
			[Alias("Url")]
		$SourceUrl,
			### SHA-256 hash that the downloaded archive should match. Validation is skipped if null, but warning is printed.
			[string]
			[ValidateScript({
				if ($_ -ne "" -and $_ -notmatch '^[a-fA-F0-9]{64}$') {
					throw "Parameter must be a SHA-256 hash (64 character hex string), got '$_'."
				}
				return $true
			})]
			[Alias("Hash")]
		$ExpectedHash,
			### If passed, only the subdirectory with passed name/path is extracted to ./app and the rest is ignored.
			### TODO: probably combine this with the top-level directory removal to make the behavior more intuitive
			[string]
		$Subdirectory = "",
			### Some servers (e.g. Apache Lounge) dislike PowerShell user agent string for some reason.
			### Set this to `Browser` to use a browser user agent string (currently Firefox).
			### Set this to `Wget` to use wget user agent string.
			[Pog.Commands.DownloadParameters+UserAgentType]
		$UserAgent = [Pog.Commands.DownloadParameters+UserAgentType]::PowerShell,
			### If you need to modify the extracted archive (e.g. remove some files), pass a scriptblock, which receives
			### a path to the extracted directory as its only argument. All modifications to the extracted files should be
			### done in this scriptblock – this ensures that the ./app directory is not left in an inconsistent state
			### in case of a crash during installation.
			[ScriptBlock]
			[Alias("Setup")]
		$SetupScript,
			### Pass this if the retrieved file is an NSIS installer
			### Currently, only thing this does is remove the `$PLUGINSDIR` output directory.
			### NOTE: NSIS installers may do some initial config, which is not ran when extracted directly.
			[switch]
		$NsisInstaller,
			### If passed, the downloaded file is directly moved to the ./app directory, without being treated as an archive and extracted.
			[switch]
		$NoArchive
	)

	if ($Subdirectory -and $NoArchive) {
		throw "Install-FromUrl: Both -Subdirectory and -NoArchive arguments were passed, at most one may be passed."
	}

	if (Test-Path $TMP_EXPAND_PATH) {
		Write-Warning "Clearing orphaned tmp installer directory, probably from a failed previous install..."
		Remove-Item -Recurse -Force $TMP_EXPAND_PATH
	}

	if (-not $ExpectedHash) {
		Write-Warning ("Downloading a file from '$SourceUrl', but no checksum was provided in the package. " +`
			"This means that we cannot be sure if the download file is the " +`
			"same one package author intended. This may or may not be a problem on its own, " +`
			"but it's better style to include a checksum, and it improves security and reproducibility.")
	}

	if (Test-Path .\app) {
		$ShouldContinue = ConfirmOverwrite "Overwrite existing package installation?" `
			("Package seems to be already installed. Do you want to overwrite the " +`
			"current installation (./app subdirectory)?`n" +`
			"Configuration and other package data will be kept.")

		if (-not $ShouldContinue) {
			throw "Not installing, user refused to overwrite existing package installation." +`
				" Do not pass -Confirm to overwrite the existing installation without confirmation."
		}

		# FIXME: when the actual waiting for unlock is implemented, this call should probably not wait,
		#  and instead just check, print out information about the locked files, and then we should start downloading
		#  and extracting in the meantime, so that when the user closes the app, he doesn't have to wait any more

		# next, we check if we can move/delete the ./app directory
		# e.g. maybe the packaged program is running and holding a lock over a file inside
		# if that would be the case, we would extract the package and then get
		#  Acess Denied error, and user would waste his time waiting for the extraction all over again
		WaitForNoLockedFilesInAppDirectory

		# do not remove the ./app directory just yet; first, we'll download the new version,
		#  and after all checks pass and we know we managed to set it up correctly,
		#  we'll delete the old version
	}

	$DownloadParams = [Pog.Commands.DownloadParameters]::new($UserAgent, $global:_Pog.InternalArguments.DownloadLowPriority)
	$Package = $global:_Pog.Package

	try {
		# 1. Download and expand (move/copy) the archive (file) to the temporary directory at $TMP_EXPAND_PATH
		RetrieveAndExpandArchive $SourceUrl $ExpectedHash -DownloadParams $DownloadParams -Package $Package -NoArchive:$NoArchive

		# 2. Setup the extracted directory into the final state, so that we can atomically move it into place
		#    This is done before replacing the ./app directory, because throwing an exception here is safe
		#    and $TMP_EXPAND_PATH will be cleaned up automatically.
		$ExtractedDir = Get-ExtractedAppDirectory -Subdirectory $Subdirectory
		PrepareNewAppDirectory $ExtractedDir -SetupScript $SetupScript -NsisInstaller:$NsisInstaller

		# 3. Move relevant parts of the extracted temporary directory to ./app directory
		if (Test-Path .\app) {
			Write-Debug "Removing previous ./app directory..."
			# check again if the directory is still lock-free
			WaitForNoLockedFilesInAppDirectory
			# no locked files, this should succeed (there's still a short race condition, but whatever...)
			Remove-Item -Recurse -Force .\app
		}

		Write-Debug "Moving extracted directory '$ExtractedDir' to './app'..."
		Move-Item -LiteralPath $ExtractedDir .\app

	} finally {
		Remove-Item -Recurse -Force -LiteralPath $TMP_EXPAND_PATH -ErrorAction Ignore
	}

	Write-Information "Package successfully installed from the downloaded archive."
}
