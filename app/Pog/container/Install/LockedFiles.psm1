using module .\..\..\lib\Utils.psm1
. $PSScriptRoot\..\..\lib\header.ps1

# http://www.nirsoft.net/utils/opened_files_view.html
$OpenedFilesViewCmd = Get-Command "OpenedFilesView" -ErrorAction Ignore
# TODO: also check if package_bin dir is in PATH, warn otherwise
if ($null -eq $OpenedFilesViewCmd) {
	throw "Could not find OpenedFilesView (command 'OpenedFilesView'), which is used during package installation. " +`
			"It is supposed to be installed as a normal Pog package, unless you manually removed it. " +`
			"If you know why this happened, please restore the package and run this command again. " +`
			"If you don't, contact Pog developers and we'll hopefully figure out where's the issue."
}

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

<# Lists processes that have a lock (an open handle without allowed sharing) on a file under $DirPath. #>
function ListProcessesLockingFiles($DirPath) {
	# OpenedFilesView always writes to a file, stdout is not supported (it's a GUI app)
	$OutFile = New-TemporaryFile
	$Procs = [Xml]::new()
	try {
		# arguments with spaces must be manually quoted
		$OFVProc = Start-Process -FilePath $OpenedFilesViewCmd -NoNewWindow -PassThru `
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
						Files = $_.Group.full_path
					}
				}
			} else {
				return [pscustomobject]@{
					ProcessInfo = $_.Name
					Files = $_.Group.full_path
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
Export function ThrowLockedFileList {
	# find out which files are locked, report to the user and throw an exception
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
	Write-Host ("`nThere is an existing package installation, which we cannot overwrite, because the following" +`
		" programs are working with files inside the installation directory:")
	$LockingProcs | % {
		Write-Host "  Files locked by '$($_.ProcessInfo)':"
		$_.Files | select -First 5 | % {
			Write-Host "    $_"
		}
		if (@($_.Files).Count -gt 5) {
			Write-Host "   ... ($($_.Files.Count) more)"
		}
	}

	throw "Cannot overwrite an existing package installation, because processes listed in the output above are working with files inside the package."
}
