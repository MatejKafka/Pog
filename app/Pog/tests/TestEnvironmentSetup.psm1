. $PSScriptRoot\..\header.ps1


function RenderTemplate($Template, $DestinationPath, [Hashtable]$TemplateData) {
    foreach ($Entry in $TemplateData.GetEnumerator()) {
        $Template = $Template.Replace("'{{$($Entry.Key)}}'", "'" + $Entry.Value.Replace("'", "''") + "'")
    }
    Set-Content $DestinationPath -Value $Template -NoNewline
}


$ManifestStr = @'
@{
    Name = '{{NAME}}'
    Architecture = "*"
    Version = '{{VERSION}}'

    Install = @{
        Url = "http://nonexistent"
        Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    }

    Enable = {
        Write-Information "Enabled '$(this.Name)', version '$(this.Version)'."
    }
}
'@

function CreateManifest {
    param($Name, $Version)
    $DirPath = "$([Pog.InternalState]::Repository.Path)\$Name\$Version"
    $null = mkdir $DirPath
    RenderTemplate $ManifestStr "$DirPath\pog.psd1" @{NAME = $Name; VERSION = $Version}
}

function CreateImportedPackage {
    param($Root, $ImportedName, $Name, $Version, $Manifest)
    $DirPath = "$Root\$ImportedName"
    $null = mkdir $DirPath
    if ($Manifest) {
        $Manifest | Set-Content "$DirPath\pog.psd1" -NoNewline
    } else {
        RenderTemplate $ManifestStr "$DirPath\pog.psd1" @{NAME = $Name; VERSION = $Version}
    }
}

function CreatePackageRoots {
    param([string[]]$Roots)
    $PackageRoots = [Pog.InternalState]::PathConfig.PackageRoots
    $null = mkdir -Force (Split-Path ($PackageRoots.PackageRootFile))
    $null = mkdir -Force $Roots
    Set-Content $PackageRoots.PackageRootFile -Value $Roots
}

function SetupNewPogTestDir {
    param([Parameter(Mandatory)][string]$TestDir)

    $TestDir = mkdir -Force $TestDir

    # clean the test directory
    rm -Recurse -Force $TestDir\*

    $null = mkdir $TestDir\data\manifest_generators, $TestDir\data\manifests, $TestDir\data\package_bin
    $null = mkdir $TestDir\cache\download_cache, $TestDir\cache\download_tmp

    # do not touch actual Pog data
    # NOTE: for this to work, this must be called before the Pog PowerShell module is imported,
    #  otherwise this throws an exception
    if (-not [Pog.InternalState]::InitDataRoot($TestDir)) {
        throw "TEST SETUP ERROR: Pog data root already configured."
    }

    # use local repository
    $null = [Pog.InternalState]::SetRepository([Pog.LocalRepository]::new("$TestDir\data\manifests"))

    return $TestDir
}


Export-ModuleMember -Function CreateManifest, CreateImportedPackage, CreatePackageRoots, SetupNewPogTestDir, RenderTemplate