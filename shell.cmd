::: This script starts up a new PowerShell instance where Pog is properly set up.
::: Use this when you do not want to integrate Pog with your system by changing environment variables.
@echo off

where /q pwsh && (
	:: use -NoExit so that the shell continues running
	pwsh       -ExecutionPolicy RemoteSigned -NoExit "%~dp0\shell.ps1" %*
) || (
	powershell -ExecutionPolicy RemoteSigned -NoExit "%~dp0\shell.ps1" %*
)
