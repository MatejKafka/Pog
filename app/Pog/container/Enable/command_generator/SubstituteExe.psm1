# Requires -Version 7
using module ..\..\..\lib\Utils.psm1
. $PSScriptRoot\..\..\..\lib\header.ps1

$KEEP_TEMPLATE = "$PSScriptRoot\templates\keepCwd_console.exe"
$KEEP_FILE_LENGTH = 105989
$KEEP_CMD_OFFSET = 0x84B0

$WITH_TEMPLATE = "$PSScriptRoot\templates\withCwd_console.exe"
$WITH_FILE_LENGTH = 107399
$WITH_WD_OFFSET = 0x8710
$WITH_CMD_OFFSET = 0x8B30


# uses precompiled binary and patches 1 or 2 strings inside to change target
# this is a bit obscure, but it allows for well-behaved substitute executables
#  and we doesn't need to ship full language compiler for this to work
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

		# the ".\" is important, otherwise our substitute exe would happily run random commands from PATH with the same name
		$Leaf = ".\" + (Split-Path -Leaf $ExePath)
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
		if ($SetWorkingDirectory) {
			Write-Debug "Substitute exe type mismatch - found substitute exe only sets target."
		} else {
			Write-Debug "Substitute exe type mismatch - found substitute exe sets working directory, only target was expected."
		}
		Write-Debug "(expected size: $WITH_FILE_LENGTH, real size: $($Stream.Length))"
		return $false
	}

	if (-not $SetWorkingDirectory) {
		$Matches = CompareFileRegion $Stream $KEEP_CMD_OFFSET $ExePath
		if (-not $Matches) {
			Write-Debug "Substitute exe target path does not match (no working directory set)."
		}
		return $Matches
	} else {
		$WdMatches = CompareFileRegion $Stream $WITH_WD_OFFSET (Split-Path $ExePath)
		if (-not $WdMatches) {
			Write-Debug "Substitute exe target working directory does not match."
			return $false
		}
		$LeafMatches = CompareFileRegion $Stream $WITH_CMD_OFFSET (".\" + (Split-Path -Leaf $ExePath))
		if (-not $LeafMatches) {
			Write-Debug "Substitute exe target path does not match."
		}
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
