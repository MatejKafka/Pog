# Requires -Version 7
using module .\..\..\lib\Utils.psm1
using module .\..\container_lib\Confirmations.psm1
using module .\LockedFiles.psm1
. $PSScriptRoot\..\..\lib\header.ps1


<# Temporary directory used for archive extraction. #>
$TMP_EXPAND_PATH = ".\.POG_INTERNAL_extraction_tmp"
<# Temporary directory where the new app directory is composed for multi-source installs before moving it in place. #>
$TMP_NEW_APP_PATH = ".\.POG_INTERNAL_app_new"
<# Temporary directory where the previous ./app directory is moved when installing
   a new version to support rollback in case of a failed install. #>
$TMP_APP_RENAME_PATH = ".\.POG_INTERNAL_app_old"
<# Temporary directory where a deleted directory is first moved so that the delete
   is an atomic operation with respect to the original location. #>
$TMP_DELETE_PATH = ".\.POG_INTERNAL_delete_tmp"


<# This function is called after the container setup is finished to run the passed manifest. #>
Export function __main {
	param($Manifest, $PackageArguments)

	if ($Manifest.Install -is [scriptblock]) {
		# run the installer scriptblock; this is only allowed for private manifests, repository packages must use a hashtable
		# see Env_Enable\__main for explanation of .GetNewClosure()
		& $Manifest.Install.GetNewClosure() @PackageArguments
	} else {
		# Install block is a hashtable of arguments to Install-FromUrl, or an array of these
		$Sources = foreach ($OrigEntry in $Manifest.Install) {
			# create a copy, do not modify the main manifest
			$Entry = $OrigEntry.Clone()
			# resolve SourceUrl/Url scriptblocks
			foreach ($Prop in @("SourceUrl", "Url")) {
				if ($Entry.ContainsKey($Prop) -and $Entry[$Prop] -is [scriptblock]) {
					# see Env_Enable\__main for explanation of .GetNewClosure()
					$Entry[$Prop] = & $Entry[$Prop].GetNewClosure()
				}
			}
			# we need [pscustomobject], otherwise piping to Install-FromUrl wouldn't work
			# (https://github.com/PowerShell/PowerShell/issues/13981)
			[pscustomobject]$Entry
		}

		$Sources | Install-FromUrl
	}
}

<# This function is called after __main finishes, even if it fails or gets interrupted. #>
Export function __cleanup {
	# nothing for now
}



#function RemoveDirectoryAtomically($Dir, [switch]$IgnoreNotFoundError) {
#	try {
#		[Pog.Native.DirectoryUtils]::DeleteDirectoryAtomically((Resolve-VirtualPath $Dir), (Resolve-VirtualPath $TMP_DELETE_PATH))
#	} catch {
#		# 0x80070002 = ERROR_FILE_NOT_FOUND
#		if ($IgnoreNotFoundError -and ($_.Exception.InnerException.HResult -eq 0x80070002)) {
#			return
#		}
#		throw
#	}
#}
#
#<# Retrieves the requested archive/file (either downloading it, or using a cached version), and extracts it to $TMP_EXPAND_PATH.
#   The extraction is not atomic – that is, if it fails or gets interrupted, $TMP_EXPAND_PATH may be left in a half-extracted state. #>
#function RetrieveAndExpandArchive($SourceUrl, $ExpectedHash, $DownloadParams, $Package, $Subdirectory, [switch]$NoArchive) {
#	using_object {Invoke-FileDownload $SourceUrl -ExpectedHash $ExpectedHash -DownloadParameters $DownloadParams -Package $Package} {
#		Write-Debug "File retrieved, extracting to '$TMP_EXPAND_PATH'..."
#
#		if (-not $NoArchive) {
#			if ($Subdirectory -eq ".") {
#				# 7zip doesn't like . and .. in subdirectory paths, but default behavior matches "."
#				$Subdirectory = $null
#			}
#			Expand-Archive7Zip $_.Path $TMP_EXPAND_PATH -Subdirectory $Subdirectory
#		} else {
#			# FIXME: bring back the optimization where a temporary directory is moved instead of a copy
#			$null = New-Item -Type Directory $TMP_EXPAND_PATH
#			Copy-Item $_.Path $TMP_EXPAND_PATH
#		}
#	}
#}
#
#function Get-ExtractedAppDirectory([string]$Subdirectory) {
#	$DirContent = ls $TMP_EXPAND_PATH
#	if (-not $Subdirectory) {
#		if (@($DirContent).Count -eq 1 -and $DirContent[0].PSIsContainer) {
#			# single directory in archive root (as is common for Linux-style archives)
#			Write-Debug "Archive root contains single directory '$DirContent', using it for './app'."
#			return $DirContent[0]
#		} else {
#			# no single subdirectory, multiple files in root (Windows-style archive)
#			Write-Debug "Archive root contains multiple items, using archive root directly for './app'."
#			return Get-Item $TMP_EXPAND_PATH
#		}
#	} else {
#		$DirPath = Join-Path $TMP_EXPAND_PATH $Subdirectory
#		Write-Debug "Using passed path inside archive: '$DirPath'."
#
#		# test if the path exists in the extracted directory
#		if (Test-Path -Type Leaf $DirPath) {
#			# use the parent directory, 7zip should have only extracted the file we're interested in
#			return (Get-Item $DirPath).Parent
#		} elseif (Test-Path -Type Container $DirPath) {
#			return Get-Item $DirPath
#		} else {
#			$Dirs = (ls $TMP_EXPAND_PATH).Name | % {"'" + $_ + "'"}
#			$Dirs = $Dirs -join ", "
#			throw "'-Subdirectory $Subdirectory' param was provided to 'Install-FromUrl' " +`
#				"in package manifest, but the directory does not exist inside the archive. " +`
#				"Root of the archive contains the following items: $Dirs."
#		}
#	}
#}
#
#function PrepareNewAppDirectory($SrcDirectory, [scriptblock]$SetupScript, [switch]$NsisInstaller) {
#	if ($NsisInstaller) {
#		$NsisPath = Join-Path $SrcDirectory '$PLUGINSDIR'
#		if (-not (Test-Path -Type Container $NsisPath)) {
#			throw "'-NsisInstaller' flag was passed to 'Install-FromUrl' in package manifest, " + `
#				"but the directory '`$PLUGINSDIR' does not exist in the extracted path (NSIS self-extracting archive should contain it)."
#		}
#		rm -Recurse -Force $NsisPath
#		Write-Debug "Removed `$PLUGINSDIR directory from the extracted NSIS installer archive."
#	}
#	if ($SetupScript) {
#		# run the -SetupScript with a changed directory
#		$OrigWD = Get-Location
#		try {
#			Set-Location $SrcDirectory
#			& $SetupScript
#		} finally {
#			Set-Location $OrigWD
#		}
#	}
#}
#
#function MoveOldAppDirectory {
#	Write-Debug "Moving the previous ./app directory to '$TMP_APP_RENAME_PATH'..."
#	# try to move the ./app directory; this will either atomically succeed, or we'll list the offending processes and exit
#	if (-not [Pog.Native.DirectoryUtils]::MoveDirectoryUnlocked((Resolve-VirtualPath ./app), (Resolve-VirtualPath $TMP_APP_RENAME_PATH))) {
#		Write-Debug "The previous ./app directory seems to be used."
#		# FIXME: better message
#		Write-Host "The package seems to be in use, trying to find offending processes..."
#		# something inside the directory is locked
#		ThrowLockedFileList
#	}
#}
#
#<#
#	.SYNOPSIS
#	Copies the relevant part of the extracted archive to the ./app directory.
#	Ensures that there are no locked files in the previous ./app directory (if it exists), and renames it to $TMP_APP_RENAME_PATH.
#
#	.DESCRIPTION
#	The function assumes that $SrcDirectory exists and contains the replacement ./app directory.
#	First, we attempt to move $SrcDirectory into place. If this succeeds, the function returns.
#	Otherwise, we attempt to move the old ./app directory out of the way (this typically only
#	fails when the installed app is currently open), and then move the new ./app directory into
#	place.
#
#	If this function does not complete, be it due to an unexpected error, a system crash, or this
#	pwsh instance getting killed, it may leave the package directory in an intermediate state.
##>
#function InstallExtractedAppVersion($SrcDirectory) {
#	if (Test-Path ./app) {
#		# move the old ./app directory out of the way
#		MoveOldAppDirectory
#	}
#	Write-Debug "Moving the extracted directory '$SrcDirectory' to './app'..."
#	[Pog.Native.DirectoryUtils]::MoveDirectoryAtomically($SrcDirectory, (Resolve-VirtualPath ./app))
#}
#
#Export function Install-FromUrl {
#	[CmdletBinding(PositionalBinding=$false)]
#	param(
#			### Source URL, from which the archive is downloaded. Redirects are supported.
#			[Parameter(Mandatory, Position=0, ValueFromPipelineByPropertyName)]
#			[string]
#			[Alias("Url")]
#		$SourceUrl,
#			### SHA-256 hash that the downloaded archive should match. Validation is skipped if null, but warning is printed.
#			[Parameter(ValueFromPipelineByPropertyName)]
#			[string]
#			[ValidateScript({
#				if ($_ -ne "" -and $_ -notmatch '^[a-fA-F0-9]{64}$') {
#					throw "Parameter must be a SHA-256 hash (64 character hex string), got '$_'."
#				}
#				return $true
#			})]
#			[Alias("Hash")]
#		$ExpectedHash,
#			### If passed, only the subdirectory with passed name/path is extracted to ./app and the rest is ignored.
#			### TODO: probably combine this with the top-level directory removal to make the behavior more intuitive
#			[Parameter(ValueFromPipelineByPropertyName)]
#			[string]
#		$Subdirectory = "",
#			### If passed, the extracted directory is moved to "./app/$Target", instead of directly to ./app.
#			[Parameter(ValueFromPipelineByPropertyName)]
#			[string]
#		$Target = "",
#			### Some servers (e.g. Apache Lounge) dislike PowerShell user agent string for some reason.
#			### Set this to `Browser` to use a browser user agent string (currently Firefox).
#			### Set this to `Wget` to use wget user agent string.
#			[Parameter(ValueFromPipelineByPropertyName)]
#			[Pog.Commands.DownloadParameters+UserAgentType]
#		$UserAgent = [Pog.Commands.DownloadParameters+UserAgentType]::PowerShell,
#			### If you need to modify the extracted archive (e.g. remove some files), pass a scriptblock, which receives
#			### a path to the extracted directory as its only argument. All modifications to the extracted files should be
#			### done in this scriptblock – this ensures that the ./app directory is not left in an inconsistent state
#			### in case of a crash during installation.
#			[Parameter(ValueFromPipelineByPropertyName)]
#			[ScriptBlock]
#			[Alias("Setup")]
#		$SetupScript,
#			### Pass this if the retrieved file is an NSIS installer
#			### Currently, only thing this does is remove the `$PLUGINSDIR` output directory.
#			### NOTE: NSIS installers may do some initial config, which is not ran when extracted directly.
#			[Parameter(ValueFromPipelineByPropertyName)]
#			[switch]
#		$NsisInstaller,
#			### If passed, the downloaded file is directly moved to the ./app directory, without being treated as an archive and extracted.
#			[Parameter(ValueFromPipelineByPropertyName)]
#			[switch]
#		$NoArchive
#	)
#
#	begin {
#		if (Test-Path $TMP_EXPAND_PATH) {
#			Write-Warning "Clearing an orphaned tmp installer directory, probably from a failed previous install..."
#			Remove-Item -Recurse -Force $TMP_EXPAND_PATH
#		}
#		if (Test-Path $TMP_NEW_APP_PATH) {
#			Write-Warning "Clearing an orphaned tmp installer directory, probably from a failed previous install..."
#			Remove-Item -Recurse -Force $TMP_NEW_APP_PATH
#		}
#		if (Test-Path $TMP_DELETE_PATH) {
#			Write-Warning "Clearing an orphaned tmp installer directory, probably from a failed previous install..."
#			# no need to be atomic here
#			Remove-Item -Recurse -Force $TMP_DELETE_PATH
#		}
#		if (Test-Path $TMP_APP_RENAME_PATH) {
#			# the installation has been interrupted before it cleaned up; to be safe, always revert to the previous version,
#			#  in case we add some post-install steps after the ./app directory is moved in place, because otherwise if we
#			#  would keep the new version, we'd have to check that the follow-up steps all finished
#			if (Test-Path ./app) {
#				Write-Warning "Clearing the new app directory from a previous interrupted install..."
#				# remove atomically, so that user doesn't see a partially deleted app directory in case this is interrupted again
#				RemoveDirectoryAtomically ./app -IgnoreNotFoundError
#			}
#			Write-Warning "Restoring the previous app directory to recover from an interrupted install..."
#			[Pog.Native.DirectoryUtils]::MoveDirectoryAtomically((Resolve-VirtualPath $TMP_APP_RENAME_PATH), (Resolve-VirtualPath ./app))
#		}
#
#		if (Test-Path .\app) {
#			$ShouldContinue = ConfirmOverwrite "Overwrite existing package installation?" `
#				("Package seems to be already installed. Do you want to overwrite the " +`
#				"current installation (./app subdirectory)?`n" +`
#				"Configuration and other package data will be kept.")
#
#			if (-not $ShouldContinue) {
#				throw "Not installing, user refused to overwrite existing package installation." +`
#					" Do not pass -Confirm to overwrite the existing installation without confirmation."
#			}
#
#			# FIXME: when the actual waiting for unlock is implemented, this call should probably not wait,
#			#  and instead just check, print out information about the locked files, and then we should start downloading
#			#  and extracting in the meantime, so that when the user closes the app, he doesn't have to wait any more
#
#			# next, we check if we can move/delete the ./app directory
#			# e.g. maybe the packaged program is running and holding a lock over a file inside
#			# if that would be the case, we would extract the package and then get
#			#  Acess Denied error, and user would waste his time waiting for the extraction all over again
#			if ([Pog.Native.DirectoryUtils]::IsDirectoryLocked((Resolve-VirtualPath ./app))) {
#				ThrowLockedFileList
#			}
#
#			# do not remove the ./app directory just yet; first, we'll download the new version,
#			#  and after all checks pass and we know we managed to set it up correctly,
#			#  we'll delete the old version
#		}
#	}
#
#	process {
#		throw "WIP, do not use"
#
#		if ($NoArchive) {
#			if ($Subdirectory) {throw "Install-FromUrl: Both -Subdirectory and -NoArchive arguments were passed, at most one may be passed."}
#			if ($SetupScript) {throw "Install-FromUrl: Both -SetupScript and -NoArchive arguments were passed, at most one may be passed."}
#		}
#
#		if (-not $ExpectedHash) {
#			Write-Warning ("Downloading a file from '$SourceUrl', but no checksum was provided in the package. " +`
#				"This means that we cannot be sure if the download file is the same one the package author " +`
#				"intended. This may or may not be a problem on its own, but it's better style to include a checksum, " +`
#				"and it improves security and reproducibility.")
#		}
#
#		$DownloadParams = [Pog.Commands.DownloadParameters]::new($UserAgent, $global:_Pog.InternalArguments.DownloadLowPriority)
#		$Package = $global:_Pog.Package
#
#		$Success = $false
#		try {
#			# 1. Download and expand (move/copy) the archive (file) to the temporary directory at $TMP_EXPAND_PATH.
#			RetrieveAndExpandArchive $SourceUrl $ExpectedHash -DownloadParams $DownloadParams -Package $Package `
#					-Subdirectory $Subdirectory -NoArchive:$NoArchive
#
#			# 2. Setup the extracted directory into the final state, so that we can atomically move it into place
#			#    This is done before replacing the ./app directory, because throwing an exception here is safe
#			#    and $TMP_EXPAND_PATH will be cleaned up automatically.
#			$ExtractedDir = Get-ExtractedAppDirectory -Subdirectory $Subdirectory
#			PrepareNewAppDirectory $ExtractedDir -SetupScript $SetupScript -NsisInstaller:$NsisInstaller
#
#			# 3. Atomically move $ExtractedDir to the ./app directory, ensuring that either the installation succeeds,
#			#    or the old version remains in place.
#			InstallExtractedAppVersion $ExtractedDir
#
#			$Success = $true
#		} finally {
#			if (-not $Success -and (Test-Path $TMP_APP_RENAME_PATH)) {
#				# the installation did not complete, move the old app directory back into place
#				RemoveDirectoryAtomically ./app -IgnoreNotFoundError
#				[Pog.Native.DirectoryUtils]::MoveDirectoryAtomically((Resolve-VirtualPath $TMP_APP_RENAME_PATH), (Resolve-VirtualPath ./app))
#			}
#			Write-Debug "Removing temporary installation directories..."
#			Remove-Item -Recurse -Force -LiteralPath $TMP_EXPAND_PATH, $TMP_NEW_APP_PATH -ErrorAction Ignore
#			RemoveDirectoryAtomically $TMP_APP_RENAME_PATH -IgnoreNotFoundError
#		}
#	}
#
#	end {
#		Write-Information "Package successfully installed from the downloaded archive."
#	}
#}