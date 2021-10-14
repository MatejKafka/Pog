# Requires -Version 7
Import-Module $PSScriptRoot"\Utils"


$ROOT = Resolve-Path $PSScriptRoot"\..\.."

$BIN_DIR = Resolve-Path $ROOT"\data\pkg_bin"
# directory where package files with known hash are cached
$DOWNLOAD_CACHE_DIR = Resolve-Path $ROOT"\cache\download_cache"
# directory where package files without known hash are downloaded
# a custom directory is used over system $env:TMP directory, because sometimes we move files
#  from this dir to download cache, and if the system directory was on a different partition,
#  this move could be needlessly expensive
$DOWNLOAD_TMP_DIR = Resolve-Path $ROOT"\cache\download_tmp"
$MANIFEST_REPO = Resolve-Path $ROOT"\data\manifests\"
$MANIFEST_GENERATOR_REPO = Resolve-Path $ROOT"\data\manifest_generators\"
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

$PACKAGE_EXPORT_DIR = ".\.pkg"

$RESOURCE_DIR = Resolve-Path $PSScriptRoot"\resources\"

$CONTAINER_SCRIPT = Resolve-Path $PSScriptRoot"\container\container.ps1"
$CONTAINER_SETUP_SCRIPT = Resolve-Path $PSScriptRoot"\container\setup_container.ps1"


$SYSTEM_START_MENU = Resolve-Path (Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\")
$USER_START_MENU = Resolve-Path (Join-Path $env:AppData "Microsoft\Windows\Start Menu\")

$APP_COMPAT_REGISTRY_DIR = "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers\"


Export-ModuleMember -Variable *