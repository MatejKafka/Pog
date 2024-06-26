using module ..\lib\Utils.psm1
. $PSScriptRoot\..\lib\header.ps1

$KNOWN_APPIDS = @{
	"{DFB65C4C-B34F-435D-AFE9-A86218684AA8}" = "WSL2" # Plan9FileSystem
	"{72075277-282A-420A-8C25-62BFCB94C71E}" = "WSL2" # something related to the previous entry, not sure what it actually does
}

$ImportedCimCmdlets = $false

function GetDllhostOwner {
	[CmdletBinding()]
	[OutputType([string])]
	param(
			[Parameter(Mandatory, ValueFromPipeline)]
			[int]
		$ProcessId
	)

	process {
		if (-not $ImportedCimCmdlets) {
			$ImportedCimCmdlets = $true
			Import-Module CimCmdlets
		}

		# Get-Process does not give use CommandLine in older PowerShell versions
		$Process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId"

		if ($Process.CommandLine -notmatch ".*DllHost.exe`"? /Processid:(\{[A-Z0-9-]{36}\})") {
			return $Process.Path
		}

		$AppId = $Matches[1]
		if ($KNOWN_APPIDS.ContainsKey($AppId)) {
			return $KNOWN_APPIDS[$AppId]
		}

		$AppIdReg = Get-Item "Registry::HKEY_CLASSES_ROOT\AppID\$AppId" -ErrorAction Ignore
		if (-not $AppIdReg) {
			return "Unknown DllHost process $AppId"
		}

		# default key, returns null if not present
		$Description = $AppIdReg.GetValue($null)
		if ($Description) {
			return $Description
		} else {
			return "Unknown DllHost process $AppId"
		}
	}
}

function Get-OFVPath {
	$OpenedFilesViewPath = [Pog.InternalState]::PathConfig.PathOpenedFilesView
	if (-not (Test-Path $OpenedFilesViewPath)) {
		return $null
	} else {
		return $OpenedFilesViewPath
	}
}

<# Lists processes that have a lock (an open handle without allowed sharing) on a file under $DirPath. #>
function ListProcessesLockingFiles($DirPath) {
	# http://www.nirsoft.net/utils/opened_files_view.html
	$OpenedFilesViewPath = Get-OFVPath
	if (-not $OpenedFilesViewPath) {
		Write-Verbose "Skipping locked file listing, since OpenedFilesView is not installed."
		return
	}

	# OpenedFilesView always writes to a file, stdout is not supported (it's a GUI app)
	$OutFile = New-TemporaryFile
	$Procs = [Xml]::new()
	try {
		# arguments with spaces must be manually quoted
		$OFVProc = Start-Process -FilePath $OpenedFilesViewPath -NoNewWindow -PassThru `
				-ArgumentList /sxml, "`"$OutFile`"", /nosort, /filefilter, "`"$(Resolve-VirtualPath $DirPath)`""

		# workaround from https://stackoverflow.com/a/23797762
		$null = $OFVProc.Handle
		$OFVProc.WaitForExit()
		if ($OFVProc.ExitCode -ne 0) {
			throw "Could not list processes locking files in '$DirPath' (OpenedFilesView returned exit code '$($OFVProc.ExitCode)')."
		}

		# the XML generated by OFV contains an invalid XML tag `<%_position>`, replace it
		# FIXME: error-prone, assumes the default UTF8 encoding, otherwise the XML might get mangled
		$OutXmlStr = (Get-Content -Raw $OutFile) -replace '(<|</)%_position>', '$1percentual_position>'
		$Procs.LoadXml($OutXmlStr)
	} finally {
		Remove-Item $OutFile -ErrorAction Ignore
	}
	if ($Procs.opened_files_list -ne "") {
		return $Procs.opened_files_list.item | Group-Object process_path | % {
			if ($_.Name -like "*DllHost.exe") {
				$_.Group | Group-Object process_id | % {
					$Owner = GetDllhostOwner $_.Name
					[pscustomobject]@{
						ProcessInfo = $Owner
						Files = $_.Group.full_path | Select-Object -Unique
					}
				}
			} else {
				return [pscustomobject]@{
					ProcessInfo = $_.Name
					Files = $_.Group.full_path | Select-Object -Unique
				}
			}
		}
	}
}

<#
Ensures that the existing .\app directory can be removed (no locked files from other processes).

Prints all processes that hold a lock over a file in an existing .\app directory, then waits until user closes them,
in a loop, until there are no locked files in the directory.
#>
function ShowLockedFileList {
	param($ForegroundColor)
	$Fg = $ForegroundColor

	# no need to check for admin rights before running OpenedFilesView, it will just report an empty list
	# TODO: probably would be better to detect the situation and suggest to the user that he can re-run
	#  the installation as admin and he'll get more information

	# find out which files are locked, report them to the user
	$LockingProcs = ListProcessesLockingFiles .\app
	if (@($LockingProcs).Count -eq 0) {
		# some process is locking files in the directory, but we don't which one; this may happen if OFV is not installed,
		# or the offending process exited between the C# check and calling OFV
		Write-Host -ForegroundColor $Fg ("There is an existing package installation, which we cannot overwrite, as there are" +`
			" file(s) opened by an unknown running process.`nIs it possible that some program from" +`
			" the package is running or that another running program is using a file from this package?")
		if (-not (Get-OFVPath)) {
			Write-Host -ForegroundColor $Fg "`nTo have Pog automatically identify the processes locking the files, install the 'OpenedFilesView' package."
		}
		return
	}

	# TODO: print more user-friendly app names
	# TODO: explain to the user what he should do to resolve the issue

	# long error messages are hard to read, because all newlines are removed;
	#  instead write out the files and then show a short error message
	Write-Host -ForegroundColor $Fg ("`nThere is an existing package installation, which we cannot overwrite, because the following" +`
		" programs are working with files inside the installation directory:")
	$LockingProcs | % {
		Write-Host -ForegroundColor $Fg "  Files locked by '$($_.ProcessInfo)':"
		$_.Files | select -First 5 | % {
			Write-Host -ForegroundColor $Fg "    $_"
		}
		if (@($_.Files).Count -gt 5) {
			Write-Host -ForegroundColor $Fg "   ... ($($_.Files.Count) more)"
		}
	}
}


Export-ModuleMember -Function ShowLockedFileList