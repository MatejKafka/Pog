[Diagnostics.CodeAnalysis.SuppressMessage("PSProvideCommentHelp", "")]
param(
    [Parameter(Mandatory)]
    [string]
    $TestDir
)

. $PSScriptRoot\..\header.ps1

$InformationPreference = "Continue"

$IsNonIteractive = [System.Environment]::GetCommandLineArgs() -eq "-noninteractive"
if ($IsNonIteractive -and (Get-Variable PSStyle -ErrorAction Ignore)) {
    # do not output ANSI escape sequences on pwsh.exe when running through Pester
    $PSStyle.OutputRendering = 'PlainText'
}

# setup test directory and import Pog
& {
    $TestDir = mkdir -Force $TestDir
    rm -Recurse -Force $TestDir\*

    $null = New-PSDrive PogTests -PSProvider FileSystem -Root $TestDir -Scope Global
    cd PogTests:\

    $null = mkdir .\data\repository, .\data\package_bin
    $null = mkdir .\cache\download_cache, .\cache\download_tmp

    # import Pog.dll so that we can override the test path below
    . $PSScriptRoot\..\LoadPogDll.ps1

    # use the test directory for everything, do not touch actual Pog data
    if (-not [Pog.InternalState]::SetTestPathConfig($TestDir)) {
        throw "TEST SETUP ERROR: Pog data root already configured."
    }

    # import the public Pog module
    Import-Module $PSScriptRoot\..
}


function RenderTemplate($Template, $DestinationPath, [Hashtable]$TemplateData) {
    foreach ($Entry in $TemplateData.GetEnumerator()) {
        $Template = $Template.Replace("'{{$($Entry.Key)}}'", "'" + $Entry.Value.Replace("'", "''") + "'")
    }
    Set-Content $DestinationPath -Value $Template -NoNewline
}

function WriteManifest($Path, $Manifest) {
    [Pog.ManifestTemplateFile]::SerializeSubstitutionFile($Path, $Manifest)
}

function WriteDefaultManifest($Path, $Name, $Version) {
    WriteManifest $Path @{
        Name = $Name
        Architecture = "*"
        Version = $Version

        Install = @{
            Url = "http://nonexistent"
            Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        }

        Enable = {
            Write-Information "Enabled '$(this.Name)', version '$(this.Version)'."
        }
    }
}

function CreateManifest($Name, $Version) {
    $Dir = mkdir -Force "$(Get-PogRepository)\$Name\$Version"
    WriteDefaultManifest $Dir\pog.psd1 $Name $Version
}

function CreateTemplateManifest($Name) {
    $Dir = mkdir -Force "$(Get-PogRepository)\$Name\.template"
    WriteManifest $Dir\pog.psd1 @{
        Name = $Name
        Architecture = "*"
        Version = '{{TEMPLATE:Version}}'

        Install = @{
            Url = '{{TEMPLATE:Url}}'
            Hash = '{{TEMPLATE:Hash}}'
        }

        Enable = {
            Write-Information "Enabled '$(this.Name)', version '$(this.Version)'."
        }
    }
}

function CreateGenerator($Name, $ListVersions, $Url = {"https://fake.url/$_"}) {
    $Dir = mkdir -Force "$(Get-PogRepository)\$Name\.template"
    WriteManifest $Dir\generator.psd1 @{
        ListVersions = $ListVersions
        _GetUrl = $Url
        Generate = {
            return [ordered]@{
                Version = $_
                Url = & $this._GetUrl
                Hash = "A" * 64
            }
        }
    }
}

function CreateImportedPackage($Root, $ImportedName, $Name, $Version, $Manifest) {
    $Dir = mkdir -Force "$Root\$ImportedName"
    if ($Manifest) {
        $Manifest | Set-Content $Dir\pog.psd1 -NoNewline
    } else {
        WriteDefaultManifest $Dir\pog.psd1 $Name $Version
    }
}

function CreatePackageRoots([string[]]$Roots) {
    $PackageRoots = [Pog.InternalState]::PathConfig.PackageRoots
    $null = mkdir -Force (Split-Path ($PackageRoots.PackageRootFile))
    $Roots = mkdir -Force $Roots | % FullName
    Set-Content $PackageRoots.PackageRootFile -Value $Roots
}

function title($Title) {
    Write-Host "--- $Title ---" -ForegroundColor White
}
