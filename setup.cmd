@echo off

:: fix https://github.com/PowerShell/PowerShell/issues/18530#issuecomment-1325691850
set PSModulePath=

powershell -noprofile -ExecutionPolicy Bypass "%~dp0\setup.ps1" %*
pause