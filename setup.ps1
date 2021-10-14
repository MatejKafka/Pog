# this module only uses library functions, so it is safe to import even during setup
Import-Module "./app/Pkg/container/Environment"

function newdir($Dir) {
    $Dir = Join-Path $PSScriptRoot $Dir
    if (Test-Path -PathType Container $Dir) {
        return
    }
    if (Test-Path $Dir) {
        # exists, but not a directory
        $null = rm -Recurse $Dir
    }
    $null = New-Item -Type Directory -Force $Dir
}

echo "Setting up all directories required by Pkg..."

# This duplicates the path definitions in app/Pkg/Paths.psm1, but we cannot use those,
#  because the file assumes all the paths already exist and fails otherwise
newdir "./data"
newdir "./cache"
# local manifest repository
newdir "./data/manifests"
newdir "./data/manifest_generators"
# directory where commands are exported; is added to PATH
newdir "./data/pkg_bin"
# downloaded package cache
newdir "./cache/download_cache"
newdir "./cache/download_tmp"

echo "Checking if package root '$(Resolve-Path .\..)' is registered..."
$ROOT_FILE_PATH = Join-Path $PSScriptRoot "./data/roots.txt"
if (-not (Test-Path -PathType Leaf $ROOT_FILE_PATH)) {
    (Resolve-Path .\..) | Set-Content $ROOT_FILE_PATH
}

echo "Setting up PATH and PSModulePath..."
# add Pkg dir to PSModulePath
Add-EnvPSModulePath (Resolve-Path "$PSScriptRoot\app")
# add pkg_bin dir to PATH
Add-EnvPath -Prepend (Resolve-Path "$PSScriptRoot\data\pkg_bin")


# ====================================================================================
# now, we should be ready to import pkg
echo "Importing Pkg...`n"
Import-Module Pkg

try {
    Enable-Pkg 7zip
} catch {
    throw ("Failed to enable the 7zip package, required for correct functioning of Pkg. " + `
            "The 7zip package should be provided with Pkg itself. Error: " + $_)
}

if (-not (Get-Command "7z" -ErrorAction Ignore)) {
    throw "Setup of 7zip was successful, but we cannot find the 7z.exe binary that should be provided by 7zip."
}

echo ""

# now, everything should be setup correctly, enable Pkg itself to validate (it doesn't do anything, just prints a success message)
Enable-Pkg Pkg

echo ""