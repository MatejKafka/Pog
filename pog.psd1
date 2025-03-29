@{
	# private, since Pog does not have an Install block
	Private = $true

	Name = "Pog"
	Version = "0.12.0"
	# why x64:
	#  1) shim binaries are compiled only for x64 (should be easy to change)
	#  2) VC redist DLLs are currently x64 (shouldn't be too hard to change)
	#  3) 7-zip and OpenedFilesView are currently installed for x64
	#     (and there's currently no infrastructure for multiple package versions for different architectures)
	Architecture = "x64"

	Enable = {
		param([switch]$NoEnv, [switch]$NoExecutionPolicy)

		# needed for Get-/Set-ExecutionPolicy
		Import-Module Microsoft.PowerShell.Security

		if ($NoEnv) {
			Write-Host "No changes to environment variables were made. To use Pog in a new PowerShell session, run:"
			Write-Host ("    . '$(Resolve-Path "./shell.ps1")'") -ForegroundColor Green
		} else {
			# add Pog dir to PSModulePath
		    Add-EnvVar PSModulePath -Prepend (Resolve-Path "./app")
		    # add binary dir to PATH
		    Add-EnvVar Path -Prepend (Resolve-Path "./data/package_bin")
		}

		if ((Get-ExecutionPolicy -Scope CurrentUser) -notin @("RemoteSigned", "Unrestricted", "Bypass")) {
			if ($NoExecutionPolicy) {
				Write-Host "No changes to execution policy were made. Pog likely won't work until you change the execution policy to at least 'RemoteSigned'."
			} else {
				# since Pog is currently not signed, we need at least RemoteSigned to run
				# https://stackoverflow.com/questions/60541618/how-to-suppress-warning-message-from-script-when-calling-set-executionpolicy/60549569#60549569
				try {Set-ExecutionPolicy -Scope CurrentUser RemoteSigned -Force} catch [System.Security.SecurityException] {}
				Write-Warning "Changed PowerShell execution policy to 'RemoteSigned' for the current user."
			}
		}

	}

	Disable = {
		Set-StrictMode -Version 3
		$ErrorActionPreference = "Stop"

		# needed for Get-/Set-ExecutionPolicy
		Import-Module Microsoft.PowerShell.Security

		# clean up the Start menu directory
		$StartMenuDir = [Pog.InternalState]::PathConfig.ExportedShortcutDir
		if (Test-Path $StartMenuDir) {
			if (ls $StartMenuDir | select -First 1) {
				Write-Warning ("The Pog start menu subdirectory at '$StartMenuDir' exists, but it's not empty. " +`
					"Either uninstall all other packages before disabling Pog, or delete the directory manually.")
			} else {
				# Start menu exists and is empty
				Remove-Item -Force -Recurse $StartMenuDir
				Write-Host "Removed the Pog Start menu subdirectory at '$StartMenuDir'."
			}
		}

		# clean up env vars
		Remove-EnvVarEntry PSModulePath (Resolve-Path "./app")
		Remove-EnvVarEntry Path (Resolve-Path "./data/package_bin")

		if ((Get-ExecutionPolicy -Scope CurrentUser) -eq "RemoteSigned") {
			# unfortunately, there's no good way to know if we should restore the execution policy
			Write-Host ("During the initial setup, Pog likely changed the execution policy for the current user to 'RemoteSigned'.`n" +`
				"If you want to revert it back to the default, run:")
			Write-Host "    Set-ExecutionPolicy Undefined -Scope CurrentUser" -ForegroundColor Green
		}
	}
}