Import-Module $PSScriptRoot"\Utils"


$ROOT = Resolve-Path $PSScriptRoot"\..\.."

$BIN_DIR = Resolve-Path $ROOT"\data\pkg_bin"
$DOWNLOAD_CACHE_DIR = Resolve-Path $ROOT"\cache\download_cache"
$MANIFEST_REPO = Resolve-Path $ROOT"\data\manifests\"
$PACKAGE_ROOT_FILE = Resolve-Path $ROOT"\data\roots.txt"

$UNRESOLVED_PACKAGE_ROOTS = [Collections.ArrayList]::new()
# cast through [array] is needed, otherwise if there is only single root, ArrayList would throw type error
$PACKAGE_ROOTS = [Collections.ArrayList][array](Get-Content $PACKAGE_ROOT_FILE | % {
	if (Test-Path $_) {
		return (Resolve-Path $_).Path
	}
	Write-Warning "Could not find package root $_. Remove it using Remove-PkgRoot or create the directory."
	[void]$UNRESOLVED_PACKAGE_ROOTS.Add($_)
})




$MANIFEST_PATHS = @(".\manifest.psd1", ".\.manifest\manifest.psd1")
$MANIFEST_CLEANUP_PATHS = @(".\manifest.psd1", ".\.manifest\")

$RESOURCE_DIR = Resolve-Path $PSScriptRoot"\resources\"

$CONTAINER_SCRIPT = Resolve-Path $PSScriptRoot"\container\container.ps1"
$CONTAINER_SETUP_SCRIPT = Resolve-Path $PSScriptRoot"\container\setup_container.ps1"


$SYSTEM_START_MENU = Resolve-Path (Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\")
$USER_START_MENU = Resolve-Path (Join-Path $env:AppData "Microsoft\Windows\Start Menu\")

$APP_COMPAT_REGISTRY_DIR = "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers\"


Export-ModuleMember -Variable *