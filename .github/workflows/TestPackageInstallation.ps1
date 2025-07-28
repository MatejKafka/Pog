Set-StrictMode -Version 3
$ErrorActionPreference = "Stop"
$InformationPreference = "Continue"

Write-Host "PowerShell v$($PSVersionTable.PSVersion)"

Import-Module ./app/Pog

Write-Host "`nExported cmdlets:"
(Get-Module Pog).ExportedCommands.Keys | % {"- $_"} | Out-Host

Write-Host "`nAvailable packages: $(@(Find-Pog -AllVersions).Count)"

$Installed = pog -PassThru fzf, zstd, Jujutsu, cloc, meson-build, ILSpy, Notepad++

Write-Host "`nInstalled:"
$Installed | % {"- $($_.PackageName) v$($_.Version) (at '$($_.Path)')"} | Out-Host


Write-Host ""
Write-Host "fzf: $(fzf --version)"
Write-Host "zstd: $(zstd --version)"
Write-Host "Jujutsu: $(jj --version)"
Write-Host "cloc: $(cloc --version)"
Write-Host "Meson: $(meson --version)"
# ILSpy and Notepad++ export a shortcut, cannot easily test it
Write-Host ""

Uninstall-Pog $Installed
Clear-PogDownloadCache -Force