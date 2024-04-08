. $PSScriptRoot\..\lib\header.ps1


function RenderTemplate($Template, $DestinationPath, [Hashtable]$TemplateData) {
    foreach ($Entry in $TemplateData.GetEnumerator()) {
        $Template = $Template.Replace("'{{$($Entry.Key)}}'", "'" + $Entry.Value.Replace("'", "''") + "'")
    }
    $null = New-Item -Path $DestinationPath -Value $Template
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

Export function CreateManifest {
    param($Name, $Version)
    $DirPath = "$([Pog.InternalState]::Repository.Path)\$Name\$Version"
    $null = mkdir $DirPath
    RenderTemplate $ManifestStr "$DirPath\pog.psd1" @{NAME = $Name; VERSION = $Version}
}

Export function CreateImportedPackage {
    param($Root, $ImportedName, $Name, $Version)
    $DirPath = "$Root\$ImportedName"
    $null = mkdir $DirPath
    RenderTemplate $ManifestStr "$DirPath\pog.psd1" @{NAME = $Name; VERSION = $Version}
}

Export function CreatePackageRoots {
    param([string[]]$Roots)
    $PackageRoots = [Pog.InternalState]::PathConfig.PackageRoots
    $null = mkdir -Force (Split-Path ($PackageRoots.PackageRootFile))
    $null = mkdir -Force $Roots
    Set-Content $PackageRoots.PackageRootFile -Value $Roots
}

Export function SetupNewPogTestDir {
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
        throw "TEST SETUP ERROR: Pog data root already configured"
    }

    # use local repository
    if (-not [Pog.InternalState]::InitRepository({[Pog.LocalRepository]::new("$TestDir\data\manifests")})) {
        throw "TEST SETUP ERROR: Pog data root already configured"
    }

    return $TestDir
}