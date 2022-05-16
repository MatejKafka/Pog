# Requires -Version 7
. $PSScriptRoot\..\..\lib\header.ps1

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


function ExtractArchive_7Zip($ArchiveFile, $TargetPath) {
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
		"-bsp1" # enable progress reports
		"-bse1" # send errors to stdout
		"-aoa" # automatically overwrite existing files
			# (should not usually occur, unless the archive is a bit malformed,
			# but NSIS installers occasionally do it for some reason)
		"-stxPE" # refuse to extract PE binaries, unless they're recognized as a self-contained installer like NSIS;
			# otherwise, if a package downloaded the program executable directly and forgot to pass -NoArchive,
			# 7zip would extract the PE segments, which is not very useful
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
		throw "Could not extract archive: '7zip' returned exit code $LastExitCode. There is likely additional output above."
	}
	if (-not (Test-Path $TargetPath)) {
		throw "'7zip' indicated success, but the extracted directory is missing. " +`
				"Seems like Pog developers fucked something up, plz send bug report."
	}
}

function ExtractArchive_Tar($ArchiveFile, $TargetPath) {
	# tar expects the target dir to exist, so we'll create it
	$null = New-Item -Type Directory $TargetPath
	# run tar
	# -f <file> = expanded archive
	# -m = do not restore modification times
	# -C <dir> = dir to extract to
	& $TarCmd --extract -f $ArchiveFile -m -C $TargetPath
	if ($LastExitCode -gt 0) {
		throw "Could not extract archive: 'tar' returned exit code $LastExitCode. There is likely additional output above."
	}
	if (-not (Test-Path $TargetPath)) {
		throw "'tar' indicated success, but the extracted directory is not present. " +`
				"Seems like Pog developers fucked something up, plz send bug report."
	}
}

Export function ExtractArchive {
    param(
            [Parameter(Mandatory)]
            [System.IO.FileInfo]
        $ArchiveFile,
            [Parameter(Mandatory)]
            [string]
        $TargetPath
    )

	# see last comment in DownloadFile for explanation of this weird try/finally construct
	$ExtractionFinished = $false
	try {
		_ExtractArchive_Inner $ArchiveFile $TargetPath
		$ExtractionFinished = $true
	} finally {
		if (-not $ExtractionFinished) {
			rm -Recurse -Force $TargetPath -ErrorAction Ignore
		}
	}
}

function _ExtractArchive_Inner($ArchiveFile, $TargetPath) {
	Write-Debug "Extracting archive (name: '$($ArchiveFile.Name)', target: '$TargetPath')."
	# use tar for .tar.gz and 7zip for everything else;
	#  tar is used for .tar.gz, because 7zip doesn't extract it in one step (it goes .tar.gz -> .tar -> content instead)
	# originally, Expand-Archive was used for .zip files, but 7zip is significantly faster and it
	#  seems to be able to extract anything I throw at it, so now it is used even for .zip
	if ($ArchiveFile.Name.EndsWith(".tar.gz")) {
		Write-Information "Extracting archive using 'tar'..."
		ExtractArchive_Tar $ArchiveFile $TargetPath
	} else {
		Write-Information "Extracting archive using '7zip'..."
		ExtractArchive_7Zip $ArchiveFile $TargetPath
	}
	Write-Debug "Archive extracted to '$TargetPath'."
}
