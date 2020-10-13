. $PSScriptRoot\..\header.ps1
Import-Module $PSScriptRoot"\..\Utils"

$KEEP_TEMPLATE = "$PSScriptRoot\templates\keepCwd.exe"
$KEEP_FILE_LENGTH = 126792
$KEEP_CMD_OFFSET = 0xB530

$WITH_TEMPLATE = "$PSScriptRoot\templates\withCwd.exe"
$WITH_FILE_LENGTH = 128378
$WITH_WD_OFFSET = 0xB990
$WITH_CMD_OFFSET = 0xBDB0


# uses precompiled binary and patches 1 or 2 strings inside to change target
# this is a bit obscure, but it allows for well-behaved substitute executables
#  and Pkg doesn't need to ship full language compiler to work
# paths are stored as UTF-8


function WriteInner($SubstitutePath, $ExePath, $SetWorkingDirectory) {
	if (-not $SetWorkingDirectory) {
		$Encoded = [Text.Encoding]::UTF8.GetBytes($ExePath)
		if ($Encoded.Count -gt 1023) {
			throw "Link path is too long, max 1023 bytes are allowed (in UTF-8 encoding): $ExePath"
		}
		
		$Stream.Position = $KEEP_CMD_OFFSET
		$Stream.Write($Encoded, 0, $Encoded.Count)
	} else {
		$Wd = Split-Path $ExePath
		$EncodedWd = [Text.Encoding]::UTF8.GetBytes($Wd)
		if ($EncodedWd.Count -gt 1023) {
			throw "Link working directory path is too long, max 1023 bytes are allowed (in UTF-8 encoding): $Wd"
		}
		
		$Leaf = Split-Path -Leaf $ExePath
		$EncodedCmd = [Text.Encoding]::UTF8.GetBytes($Leaf)
		if ($EncodedCmd.Count -gt 259) {
			throw "Command target name is too long, max 259 bytes are allowed (in UTF-8 encoding): $Leaf"
		}
		
		# write working directory
		$Stream.Position = $WITH_WD_OFFSET
		$Stream.Write($EncodedWd, 0, $EncodedWd.Count)
		
		# write exe
		$Stream.Position = $WITH_CMD_OFFSET
		$Stream.Write($EncodedCmd, 0, $EncodedCmd.Count)
	}
}


Export function Write-SubstituteExe {
	param(
			[Parameter(Mandatory)]
			[string]
		$SubstitutePath,
			[Parameter(Mandatory)]
			[ValidateScript({Test-Path -Type Leaf $_})]
			[string]
		$ExePath,
			[switch]
		$SetWorkingDirectory
	)
	
	$UsedTemplate = if ($SetWorkingDirectory) {$WITH_TEMPLATE} else {$KEEP_TEMPLATE}
	
	$ExePath = Resolve-Path $ExePath
	$SubstitutePath = Resolve-VirtualPath $SubstitutePath
	
	Copy-Item $UsedTemplate $SubstitutePath
	$Stream = [IO.File]::OpenWrite($SubstitutePath)
	
	try {
		WriteInner $SubstitutePath $ExePath $SetWorkingDirectory
	} catch {
		$Stream.Close()
		Remove-Item $SubstitutePath
		throw $_
	} finally {
		$Stream.Close()
	}
}


function CompareFileRegion($Stream, $Offset, [string]$ComparedText) {
	$Encoded = [Text.Encoding]::UTF8.GetBytes($ComparedText)
	
	if ($Encoded.Count -gt 1023) {
		# too long
		return $false
	}
	
	$Stream.Position = $Offset
	$b = [byte[]]::new($Encoded.Count)
	[void]$Stream.Read($b, 0, $b.Count)
	$term = [byte[]]::new(1)
	[void]$Stream.Read($term, 0, 1)
	
	if ($term[0] -ne 0) {
		# last byte is not null, written value is longer than it should be
		return $false
	}
	return [Linq.Enumerable]::SequenceEqual($Encoded, $b)
}


function TestInner($Stream, $ExePath, $SetWorkingDirectory) {
	if (($Stream.Length -eq $WITH_FILE_LENGTH) -ne $SetWorkingDirectory) {
		# mismatch (one keeps working directory, other one changes it)
		return $false
	}
	
	if (-not $SetWorkingDirectory) {
		return CompareFileRegion $Stream $KEEP_CMD_OFFSET $ExePath
	} else {
		$WdMatches = CompareFileRegion $Stream $WITH_WD_OFFSET (Split-Path $ExePath)
		if (-not $WdMatches) {return $false}
		$LeafMatches = CompareFileRegion $Stream $WITH_CMD_OFFSET (Split-Path -Leaf $ExePath)
		return $LeafMatches
	}
}


Export function Test-SubstituteExe {
	param(
			[Parameter(Mandatory)]
			[ValidateScript({Test-Path -Type Leaf $_})]
			[string]
		$SubstitutePath,
			[Parameter(Mandatory)]
			[ValidateScript({Test-Path -Type Leaf $_})]
			[string]
		$ExePath,
			[switch]
		$SetWorkingDirectory
	)
	
	$ExePath = Resolve-Path $ExePath
	$SubstitutePath = Resolve-Path $SubstitutePath
	
	$Stream = [IO.File]::OpenRead($SubstitutePath)
	try {
		return TestInner $Stream $ExePath $SetWorkingDirectory
	} finally {
		$Stream.Close()
	}
}