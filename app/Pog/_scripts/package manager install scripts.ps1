# package manager install scripts

Write-Information "`nInstalling Pog..."
iex "& {$(irm https://pog.matejkafka.com/install.ps1)} -NoConfirm"

Write-Information "`nInstalling Scoop..."
Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression

Write-Information "`nInstalling Chocolatey..."
Set-ExecutionPolicy Bypass -Scope Process -Force; [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))

Write-Information "`nInstalling WinGet..."
curl.exe https://aka.ms/getwinget --output winget.msixbundle --location
curl.exe https://aka.ms/Microsoft.VCLibs.x64.14.00.Desktop.appx --output vclibs.appx --location
curl.exe https://github.com/microsoft/microsoft-ui-xaml/releases/download/v2.8.6/Microsoft.UI.Xaml.2.8.x64.appx --output xaml.appx --location
& {
    $ProgressPreference = 'SilentlyContinue'
    $Files = "vclibs.appx", "xaml.appx", "winget.msixbundle"
    $Files | Add-AppxPackage
    $Files | rm
}