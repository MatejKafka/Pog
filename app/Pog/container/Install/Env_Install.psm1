# Requires -Version 7
. $PSScriptRoot\..\..\lib\header.ps1
Import-Module $PSScriptRoot\..\container_lib\Confirmations
Import-Module $PSScriptRoot\ExtractArchive
Import-Module $PSScriptRoot\FileDownloader

Export-ModuleMember -Function Confirm-Action


$TMP_EXPAND_PATH = ".\.install_tmp"

# http://www.nirsoft.net/utils/opened_files_view.html
$OpenedFilesViewCmd = Get-Command "OpenedFilesView" -ErrorAction Ignore
# TODO: also check if package_bin dir is in PATH, warn otherwise
if ($null -eq $OpenedFilesViewCmd) {
	throw "Could not find OpenedFilesView (command 'OpenedFilesView'), which is used during package installation. " +`
			"It is supposed to be installed as a normal Pog package, unless you manually removed it. " +`
			"If you know why this happened, please restore the package and run this command again. " +`
			"If you don't, contact Pog developers and we'll hopefully figure out where's the issue."
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

<# This function is called after the Install script finishes. #>
Export function __cleanup {
	# nothing for now
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

<# Checks if any file in the passed directory is locked by a process (has an open handle without allowed sharing). #>
function CheckForLockedFiles($DirPath) {
	foreach ($file in Get-ChildItem -Recurse -File $DirPath) {
		# try to open each file for writing; if it fails with HRESULT == 0x80070020,
		#  we know another process holds a lock over the file
		try {
			# TODO: use the low-level version which returns handle, we don't need the C# file stream, and this is a very hot loop
			[System.IO.File]::Open($file, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Write).Close()
		} catch {
			$InnerException = $_.Exception.InnerException
			if ($null -ne $InnerException -and $InnerException.HResult -eq 0x80070020) {
				return $true # another process is holding a lock
			} else {
				throw # another error
			}
		}
	}
	return $false
}

<# Lists processes that have a lock (an open handle without allowed sharing) on a file under $DirPath. #>
function ListProcessesLockingFiles($DirPath) {
	# OpenedFilesView always writes to a file, stdout is not supported (it's a GUI app)
	$OutFile = New-TemporaryFile
	$Procs = [Xml]::new()
	try {
		# arguments with spaces must be manually quoted
		$OFVProc = Start-Process -FilePath $OpenedFilesViewCmd -Wait -NoNewWindow -PassThru `
				-ArgumentList /sxml, "`"$OutFile`"", /nosort, /filefilter, "`"$(Resolve-Path $DirPath)`""
		if ($OFVProc.ExitCode -ne 0) {
			throw "Could not list processes locking files in '$DirPath' (OpenedFilesView returned exit code '$($Proc.ExitCode)')."
		}
		# the XML generated by OFV contains an invalid XML tag `<%_position>`, replace it
		# FIXME: error-prone, assumes the default UTF8 encoding, otherwise the XML might get mangled
		$OutXmlStr = (Get-Content -Raw $OutFile) -replace '(<|</)%_position>', '$1percentual_position>'
		$Procs.LoadXml($OutXmlStr)
	} finally {
		rm $OutFile -ErrorAction Ignore
	}
	if ($Procs.opened_files_list -ne "") {
		return $Procs.opened_files_list.item | Group-Object process_path | % {[pscustomobject]@{
				ProcessPath = $_.Name
				Files = $_.Group.full_path
			}}
	}
}

<#
Ensures that the existing .\app directory can be removed (no locked files from other processes).

Prints all processes that hold a lock over a file in an existing .\app directory, then waits until user closes them,
in a loop, until there are no locked files in the directory.
#>
function WaitForNoLockedFilesInAppDirectory {
	Write-Debug "Checking if there are any locked files in the existing './app' directory..."
	if (-not (CheckForLockedFiles .\app)) {
		# no locked files
		return
	}

	# there are some locked files, find out which, report to the user and throw an exception
	$LockingProcs = ListProcessesLockingFiles .\app
	if (@($LockingProcs).Count -eq 0) {
		# some process is locking files in the directory, but we don't which one
		# therefore, we cannot wait for it; only reasonable option I see is to throw an error and let the user handle it
		# I don't see why this should happen (unless some process exits between the two checks, or there's an error in OFV),
		# so this is here just in case
		throw "There is an existing package installation, which we cannot overwrite, as there are" +`
			" file(s) opened by an unknown running process. Is it possible that some program from" +`
			" the package is running or that another running program is using a file from this package?" +`
			" To resolve the issue, stop all running programs that are working with files in the package directory," +`
			" and then run the installation again."
	}

	# TODO: print more user-friendly app names
	# TODO: instead of throwing an error, list the offending processes and then wait until the user stops them
	# TODO: explain to the user what he should do to resolve the issue

	# long error messages are hard to read, because all newlines are removed;
	#  instead write out the files and then show a short error message
	echo ("`nThere is an existing package installation, which we cannot overwrite, because the following" +`
		" programs are working with files inside the installation directory:")
	$LockingProcs | % {
		echo "  Files locked by '$($_.ProcessPath)':"
		$_.Files | select -First 5 | % {
			echo "    $_"
		}
		if (@($_.Files).Count -gt 5) {
			echo "   ... ($($_.Files.Count) more)"
		}
	}

	throw "Cannot overwrite an existing package installation, because processes listed in the output above are working with files inside the package."
}

Export function Install-FromUrl {
	[CmdletBinding(PositionalBinding=$false)]
	param(
			[Parameter(Mandatory, Position=0)]
			[Alias("Url")]
			[string]
		$SrcUrl,
			<#
			SHA-256 hash that the downloaded archive should match.
			Validation is skipped if null, but warning is printed.

			If '?' is passed, nothing will be installed, we will download the file, compute the SHA-256 hash and print it out.
			This is intended to be used when writing a new manifest and trying to figure out the hash of the file.
			#>
			[Alias("Hash")]
			[string]
			[ValidateScript({
				if ($_ -ne "?" -and $_ -notmatch '^(\-|[a-fA-F0-9]{64})$') {
					throw "Parameter must be hex string (SHA-256 hash), 64 characters long (or '?'), got '$_'."
				}
				return $true
			})]
		$ExpectedHash,
			<# If passed, only the subdirectory with passed name/path is extracted to ./app and the rest is ignored. #>
			[string]
		$Subdirectory = "",
			<# Some servers (e.g. Apache Lounge) dislike PowerShell user agent string for some reason.
			   Set this to `Browser` to use a browser user agent string (currently Firefox).
			   Set this to `Wget` to use wget user agent string. #>
			[UserAgentType]
		$UserAgent = [UserAgentType]::PowerShell,
			<#
			If you need to modify the extracted archive (e.g. remove some files), pass a scriptblock, which receives
			a path to the extracted directory as its only argument. All modifications to the extracted files should be
			done in this scriptblock – this ensures that the ./app directory is not left in an inconsistent state
			in case of a crash during installation.
			#>
			[ScriptBlock]
		$SetupScript,
			<# Pass this if the retrieved file is an NSIS installer
			   Currently, only thing this does is remove the `$PLUGINSDIR` output directory.
			   NOTE: NSIS installers may do some initial config, which is skipped when extracted directly. #>
			[switch]
		$NsisInstaller,
			<# If passed, the downloaded file is directly moved to the ./app directory, without being treated as an archive and extracted. #>
			[switch]
		$NoArchive
	)

	if ($Subdirectory -and $NoArchive) {
		throw "Install-FromUrl: Both -Subdirectory and -NoArchive arguments were passed, at most one may be passed."
	}

	if (Test-Path $TMP_EXPAND_PATH) {
		Write-Warning "Clearing orphaned tmp installer directory, probably from failed previous install..."
		rm -Recurse -Force $TMP_EXPAND_PATH
	}

	# these parameters are directly forwarded to the `DownloadFile` function in .\FileDownloader.psm1
	$DownloadParams = @{
		UserAgent = $UserAgent
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
		# it seems more ergonomic to exit than return (so that no further installation steps are executed)
		exit
	}

	if (Test-Path .\app) {
		$ShouldContinue = ConfirmOverwrite "Overwrite existing package installation?" `
			("Package seems to be already installed. Do you want to overwrite " +`
			"current installation (./app subdirectory)?`n" +`
			"Configuration and other package data will be kept.")

		if (-not $ShouldContinue) {
			throw "Not installing, user refused to overwrite existing package installation." +`
					" Pass -AllowOverwrite to overwrite the existing installation without confirmation."
		}

		# next, we check if we can move/delete the ./app directory
		# e.g. maybe the packaged program is running and holding a lock over a file inside
		# if that would be the case, we would extract the package and then get
		#  Acess Denied error, and user would waste his time waiting for the extraction all over again
		# this command outputs data directly, don't $null= it
		WaitForNoLockedFilesInAppDirectory

		# do not remove the ./app directory just yet; first, we'll download the new version,
		#  and after all checks pass and we know we managed to set it up correctly,
		#  we'll delete the old version
	}


	# 1. Download and expand (move/copy) the archive (file) to the temporary directory at $TMP_EXPAND_PATH
	Write-Information "Retrieving file from '$SrcUrl' (or local cache)..."
	if (-not [string]::IsNullOrEmpty($ExpectedHash)) {
		# we have fixed hash, we can use download cache
		$DownloadedFile = Invoke-CachedFileDownload $SrcUrl `
				-ExpectedHash $ExpectedHash.ToUpper() -DownloadParams $DownloadParams
		Write-Debug "File correctly retrieved, expanding to '$TMP_EXPAND_PATH'..."
		if (-not $NoArchive) {
			ExtractArchive $DownloadedFile $TMP_EXPAND_PATH
		} else {
			$null = New-Item -Type Directory $TMP_EXPAND_PATH
			Copy-Item $DownloadedFile $TMP_EXPAND_PATH
		}
	} else {
		Write-Warning ("Downloading a file from '${SrcUrl}', but no checksum was provided in the package " +`
				"(passed to 'Install-FromUrl'). This means that we cannot be sure if the download file is the " +`
				"same one package author intended. This may or may not be a problem on its own, " +`
				"but it's better style to include a checksum, and improves security and reproducibility.")
		# the hash is not set, cannot safely cache the file
		$TmpDir, $DownloadedFile = Invoke-TmpFileDownload $SrcUrl -DownloadParams $DownloadParams
		if (-not $NoArchive) {
			try {
				ExtractArchive $DownloadedFile $TMP_EXPAND_PATH
			} finally {
				# remove the temporary dir (including the file) after we finish
				Remove-Item -Recurse $TmpDir
				Write-Debug "Removed temporary downloaded archive '$DownloadedFile'."
			}
		} else {
			# move the temporary directory directly, with the file inside; this avoids unnecessary copying
			Move-Item $TmpDir $TMP_EXPAND_PATH
			Write-Debug "Moved temporary downloaded archive to '$TMP_EXPAND_PATH'."
		}
	}

	# 2. Move relevant parts of the extracted temporary directory to ./app directory
	try {
		$ExtractedDir = Get-ExtractedDirPath -Subdirectory $Subdirectory

		if (Test-Path .\app) {
			Write-Debug "Removing previous ./app directory..."
			# check again if the directory is still lock-free
			WaitForNoLockedFilesInAppDirectory
			# no locked files, this should succeed (there's still a short race condition, but whatever...)
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
	if ($null -ne $SetupScript) {
		# FIXME: move this to the correct place
		# TODO: set working directory?
		& $SetupScript (Resolve-Path ./app)
	}

	Write-Information "Package successfully installed from downloaded archive."
}
