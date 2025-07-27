### .DESCRIPTION
### This is the main script module of Pog. Most of the actual functionality is implemented in C# in `Pog.dll`,
### which is loaded below. Cmdlets defined in this module are somewhat half-baked - I typically first implement
### a cmdlet here, see how it works in practice and later rewrite it in C# when I have a clearer idea about the design.

using module .\Utils.psm1
. $PSScriptRoot\header.ps1
Import-Module (Get-PogDll)

# if there are any missing package roots, show a warning
foreach ($r in [Pog.InternalState]::PathConfig.PackageRoots.MissingPackageRoots) {
    Write-Warning ("Could not find package root '$r'. Create the directory, or remove it" `
            + " from the package root list using the 'Edit-PogRoot' command.")
}


# functions to programmatically add/remove package roots are intentionally not provided, because it is a bit non-trivial
#  to get the file updates right from a concurrency perspective
# TODO: ^ figure out how to provide the functions safely
function Edit-PogRoot {
    ### .SYNOPSIS
    ### Opens the configuration file listing package roots in a text editor.
    [CmdletBinding()]
    param()

    $Path = [Pog.InternalState]::ImportedPackageManager.PackageRoots.PackageRootFile
    Write-Host "Opening the package root list at '$Path' for editing in a text editor..."
    Write-Host "Each line should contain a single path to the package root directory."
    Write-Host "Both absolute and relative paths are allowed, relative paths are resolved from the path of the edited file."
    # this opens the file for editing in a text editor (it's a .txt file)
    Start-Process $Path
}

filter RenderPsd1($Path) {
    # slight misuse of the template serializer, but it works quite well for our purposes
    return [Pog.ManifestTemplateFile]::SerializeSubstitutionFile($Path, $_)
}

function RenderPackageManifest($Path, $PackageName, $Version, [switch]$Templated, [switch]$TemplatedUrl) {
    [ordered]@{
        Name = $PackageName
        Architecture = 'x64'
        Version = if ($Templated) {'{{TEMPLATE:Version}}'} else {$Version}

        _1 = [Pog.ManifestTemplateFile+SerializerEmptyLine]::new()
        Install = [ordered]@{
            Url = if ($TemplatedUrl) {'{{TEMPLATE:Url}}'} else {{$V = $this.Version; ""}}
            Hash = if ($Templated) {'{{TEMPLATE:Hash}}'} else {''}
        }

        _2 = [Pog.ManifestTemplateFile+SerializerEmptyLine]::new()
        Enable = {

        }
    } | RenderPsd1 $Path
}

function RenderPackageGenerator($Path) {
    [ordered]@{
        ListVersions = {

        }

        _1 = [Pog.ManifestTemplateFile+SerializerEmptyLine]::new()
        Generate = {
            return [ordered]@{
                Version = $_.Version
                Url = ''
                Hash = ''
            }
        }
    } | RenderPsd1 $Path
}

# TODO: support creating new versions of existing packages (either create a blank package, or copy latest version and modify the Version field);
#  also support automatically retrieving the hash and patching the manifest; ideally, for templated packages in the default form
#  (templated Version + Hash), dev should be able to just call `New-PogPackage 7zip 30.01` and get a finished package without any further tweaking
function New-PogRepositoryPackage {
    ### .SYNOPSIS
    ### Create a new manifest in the configured package repository.
    ### Only supported for local repositories.
    [CmdletBinding(PositionalBinding=$false)]
    [OutputType([Pog.LocalRepositoryPackage])]
    param(
            ### Name of the new manifest. No manifest under that package name should exist.
            [Parameter(Mandatory, Position=0)]
            [Pog.Verify+PackageName()]
            [string]
        $PackageName,
            ### Versions of the new manifest to create.
            [Parameter(Position=1)]
            [Pog.PackageVersion[]]
        $Version,
            ### Specifies what type of package to generate.
            ### - Direct = package with a full manifest for each version
            ### - Templated = package with a manifest template that is rendered at import time with version-specific values
            ### - Generated = templated package that also includes a generator for automatically updating the package
            [ValidateSet('Direct', 'Templated', 'Generated')]
            [string]
        $Type = 'Templated'
    )

    begin {
        if ($Type -eq "Direct" -and -not $Version) {
            throw "When creating a '-Type Direct' package, you must specify the version of the package to create."
        }

        if ([Pog.InternalState]::Repository -isnot [Pog.LocalRepository]) {
            throw ("Creating new packages is only supported for local repositories, not remote. To add a package to a remote repository, " +`
                "create it in a local repository and then add it to the remote repository (typically with a Git push / PR).")
        }

        $c = [Pog.InternalState]::Repository.GetPackage($PackageName, $true, $false)
        if ($c.Exists) {
            throw "Package '$($c.PackageName)' already exists in the repository at '$($c.Path)'.'"
        }

        $null = New-Item -Type Directory $c.Path

        if ($Type -in "Templated", "Generated") {
            $null = New-Item -Type Directory $c.TemplateDirPath
            RenderPackageManifest $c.TemplatePath $c.PackageName -Templated -TemplatedUrl:($Type -in "Generated")
        }

        if ($Type -in "Generated") {
            RenderPackageGenerator $c.GeneratorPath
        }

        foreach ($v in $Version) {
            $p = $c.GetVersionPackage($v, $false)
            switch ($Type) {
                "Direct" {
                    # create manifest dir for version
                    $null = New-Item -Type Directory $p.Path
                    RenderPackageManifest $p.ManifestPath $p.PackageName $p.Version
                }
                "Templated" {
                    [ordered]@{Version = $p.Version; Hash = ''} | RenderPsd1 $p.ManifestPath
                }
                "Generated" {
                    [ordered]@{Version = $p.Version; Url = ''; Hash = ''} | RenderPsd1 $p.ManifestPath
                }
            }
            echo $p
        }
    }
}

function New-PogPackage {
    ### .SYNOPSIS
    ### Creates a new empty package directory with a default manifest.
    [CmdletBinding()]
    [OutputType([Pog.ImportedPackage])]
    param(
            ### Name of the new package.
            [Parameter(Mandatory)]
            [Pog.Verify+PackageName()]
            [string]
        $PackageName,
            ### Package root where the package is created. By default, the first package root is used.
            [ArgumentCompleter([Pog.PSAttributes.ValidPackageRootPathCompleter])]
            [string]
        $PackageRoot
    )

    begin {
        $PackageRoot = if (-not $PackageRoot) {
            [Pog.InternalState]::ImportedPackageManager.DefaultPackageRoot
        } else {
            try {[Pog.InternalState]::ImportedPackageManager.ResolveValidPackageRoot($PackageRoot)}
            catch [Pog.InvalidPackageRootException] {
                $PSCmdlet.ThrowTerminatingError($_)
            }
        }

        $p = [Pog.InternalState]::ImportedPackageManager.GetPackage($PackageName, $PackageRoot, $false, $false, $false)
        if ($p.Exists) {
            throw "Package already exists: $($p.Path)"
        }

        # create the package dir
        $null = New-Item -Type Directory $p.Path
        [ordered]@{
            Private = $true
            Enable = {

            }
        } | RenderPsd1 $p.ManifestPath

        return $p
    }
}


# this is a temporary function which is not very well thought through
function Update-Pog {
    ### .SYNOPSIS
    ### Lists outdated installed packages and updates selected packages to the latest version.
    [CmdletBinding()]
    param(
            [ArgumentCompleter([Pog.PSAttributes.ImportedPackageNameCompleter])]
            [Pog.Verify+PackageName()]
            [string[]]
        $PackageName,
            [switch]
            ### Only list outdated packages, do not update them.
        $ListOnly,
            [switch]
            ### If passed, also download the package manifest and verify that the installed package manifest is up-to-date.
        $ManifestCheck,
            [switch]
        $Force,
            ### If passed, only update frozen packages, which are otherwise ignored.
            [switch]
        $Frozen
    )

    $ImportedPackages = Get-Pog $PackageName | ? {$_.Version -and $_.ManifestName -and $_.UserManifest.Frozen -eq $Frozen}

    $RepositoryPackageMap = @{}
    $ImportedPackages | % ManifestName `
        | select -Unique `
        | Find-Pog -LoadManifest:$ManifestCheck -ErrorAction Ignore `
        | % {$RepositoryPackageMap[$_.PackageName] = $_}

    $OutdatedPackages = $ImportedPackages | % {
        $r = $RepositoryPackageMap[$_.ManifestName]
        if (-not $r) {
            return
        }

        if ($r.Version -gt $_.Version -or ($ManifestCheck -and $r.Version -eq $_.Version -and -not $r.MatchesImportedManifest($_))) {
            return [pscustomobject]@{
                PackageName = $_.PackageName
                CurrentVersion = $_.Version
                LatestVersion = $r.Version
                Target = $_
            }
        }
    }

    $SelectedPackages = if ($Force) {
        $OutdatedPackages | % Target
    } else {
        $OutdatedPackages | Out-GridView -PassThru -Title "Outdated packages" | % Target
    }

    if ($ListOnly) {
        return $SelectedPackages
    } else {
        # support parameter defaults for `Import-Pog` set by the user
        #  (quite hacky, but the result behaves consistently with `Invoke-Pog`, which implicitly
        #  invokes `Import-Pog` in the caller's scope, so it picks up the caller's defaults)
        $PSDefaultParameterValues = $global:PSDefaultParameterValues

        # -Force because user already confirmed the update
        $SelectedPackages | Invoke-Pog -Force
    }
}

class DownloadCacheEntry {
    [string]$PackageName
    # FIXME: this should be a Pog.PackageVersion (but Pog.dll is not loaded when this is parsed)
    [string]$Version
    [string]$Hash
    [string]$FileName

    # [ulong] is not supported by PowerShell 5
    hidden [UInt64]$Size
    # with a `Path` field, this type can be piped to `rm -Recurse`
    hidden [string]$Path
}

filter ListEntries($FilterSb) {
    foreach ($Source in $_.SourcePackages | ? $FilterSb) {
        [DownloadCacheEntry]@{
            PackageName = $Source.PackageName
            Version = $Source.ManifestVersion
            Hash = $_.EntryKey
            FileName = [System.IO.Path]::GetFileName($_.Path)

            Size = $_.Size
            Path = [System.IO.Path]::GetDirectoryName($_.Path)
        }
    }
}

function Get-PogDownloadCache {
    [CmdletBinding(PositionalBinding=$false)]
    [OutputType([DownloadCacheEntry])]
    param(
            [Parameter(Position=0, ValueFromPipeline, ValueFromPipelineByPropertyName)]
            [ArgumentCompleter([Pog.PSAttributes.DownloadCachePackageNameCompleter])]
            [string[]]
        $PackageName
    )

    begin {
        $Entries = [array][Pog.InternalState]::DownloadCache.EnumerateEntries()

        if (-not $MyInvocation.ExpectingInput -and -not $PackageName) {
            $Entries | ListEntries {$true}
        }
    }

    process {
        # TODO: should we sort output by size?
        foreach ($pn in $PackageName) {
            $Result = $Entries | ListEntries {$_.PackageName -eq $pn} | sort Version -Descending
            if ($Result) {
                echo $Result
            } else {
                # hacky way to create an ErrorRecord
                $e = try {throw "No download cache entries found for package '$pn'."} catch {$_}
                $PSCmdlet.WriteError($e)
            }
        }
    }
}