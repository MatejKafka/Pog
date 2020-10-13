. $PSScriptRoot\..\header.ps1


Export function Install-FromUrl {
	param(
			[Parameter(Mandatory)]
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
		if (-not $global:Pkg_AllowOverwrite) {
			throw "./app directory already exists; pass -AllowOverwrite to overwrite it."
		}
		echo "Removing previous ./app directory..."
		rm -Recurse -Force .\app
	}
	
	echo "Downloading archive from $SrcUrl"
	$Tmp = Invoke-TmpFileDownload $SrcUrl -ExpectedHash $ExpectedHash
	
	try {
		if ($Force7zip -or $Tmp.Name.EndsWith(".7z") -or $Tmp.Name.EndsWith(".7z.exe")) {
			echo "Expanding downloaded archive using 7zip..."
			# run 7zip with silenced status reports
			& $PSScriptRoot\bin\7za.exe x $Tmp ("-o" + $TMP_EXPAND_PATH) -bso0 -bsp0
			if ($LastExitCode -gt 0) {
				throw "Could not expand archive: 7za.exe returned exit code $LastExitCode. There is likely additional output above."
			}
		} else {
			echo "Expanding downloaded archive..."
			Expand-Archive -Path $Tmp -DestinationPath $TMP_EXPAND_PATH -Force
		}
	} finally {
		rm $Tmp
	}
	
	if ($NoSubdirectory) {
		# use files from the root of archive directly
		Rename-Item $TMP_EXPAND_PATH .\app
	} else {
		# there should be a single folder here
		mv (ls $TMP_EXPAND_PATH) .\app
		rm $TMP_EXPAND_PATH
	}
	
	echo "Package successfully installed from downloaded archive."
}


Export function Invoke-TmpFileDownload {
	param(
			[Parameter(Mandatory)]
			[string]
		$SrcUrl,
			# If set, generated file will have given extension.
			# If not set, file name from the URL (last segment of path) will be appended to the random file name as "extension".
			[string]
		$Extension,
			[string]
		$ExpectedHash # SHA256 hash of expected file
	)
	
	# TODO: automatically clean up the temp files on installer exit
	#  (possibly by storing names of all generated files and adding exit hook to delete them)
	
	$Suffix = if (-not [string]::IsNullOrEmpty($Extension)) {$Extension}
		# use file name from the URL
		else {([uri]$SrcUrl).Segments[-1].Replace("/", "").Replace("\", "")}
	
	while ($true) {
		try {
			$TmpPath = Join-Path ([System.IO.Path]::GetTempPath()) ((New-Guid).Guid + "-" + $Suffix)
			$TmpFile = New-item $TmpPath
			break
		} catch {}
	}
	
	# we have a temp file with unique name and requested extension, download content
	Invoke-WebRequest $SrcUrl -OutFile $TmpFile
	
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