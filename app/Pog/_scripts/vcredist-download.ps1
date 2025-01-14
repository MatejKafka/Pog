# NOTE: this script is used in CI

### Downloads the vcredist.x64.exe packages from $Url, extracts the contained DLLs and stores them in $OutDir.
###
### Potentially relevant URLs:
### - latest vcredist 140: https://aka.ms/vs/17/release/vc_redist.x64.exe
### - latest vcredist 120: https://aka.ms/highdpimfc2013x64enu (as of January 2025, unlikely to change)
### - docs: https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170
param(
        [Parameter(Mandatory)]
        [string[]]
    $Url,
        [Parameter(Mandatory)]
        [string]
    $OutDir,
        [switch]
    $AdditionalLibraries,
        [switch]
    $Clean
)

$TmpDir = mkdir ($env:TEMP + "\PogVcRedist-" + (New-Guid).ToString())
try {
    $null = mkdir $TmpDir\extracted_cabs

    $Url | % {
        # download the vcredist.exe binary
        iwr $_ -OutFile $TmpDir\vcredist.exe

        # extract the .cab files using WiX dark.exe
        $null = dark -nologo -x $TmpDir\extracted_installer $TmpDir\vcredist.exe

        $DirPattern = if ($AdditionalLibraries) {"*_amd64"} else {"vcRuntimeMinimum_amd64"}
        $Cabs = ls $TmpDir\extracted_installer\AttachedContainer\packages\$DirPattern\cab1.cab

        # extract DLLs from the extracted CABs
        $Cabs | % {
            # prefer expand.exe, it's a Windows built-in, so we don't need to install it in CI
            $null = expand.exe -F:* $_ $TmpDir\extracted_cabs
            #$null = 7z x $_ ("-o" + "$TmpDir\extracted_cabs")
        }

        rm -Recurse $TmpDir\extracted_installer
    }

    $null = mkdir -Force $OutDir
    if ($Clean) {
        rm $OutDir\*.dll
    }

    # clean up file names and copy them to the output directory
    ls $TmpDir\extracted_cabs | % {
        $FixedName = switch -Regex ($_.Name) {
            # vcredist 120
            '^F_CENTRAL_(.*)_x64$' {$Matches[1] + ".dll"}
            # vcredist 140
            '^(.*).dll_amd64$' {$Matches[1] + ".dll"}
            # we do not support anything older
            default {throw "Unknown name pattern: $_"}
        }
        Move-Item $_ $OutDir\$FixedName -Force -PassThru
    }
} finally {
    rm -Force -Recurse $TmpDir
}