### This is the main script module of Pog. Most of the actual functionality is implemented in C# in `Pog.dll`,
### which is loaded in `header.ps1`. Cmdlets defined in this module are somewhat half-baked - I typically first
### implement a cmdlet here, see how it works in practice and later rewrite it in C# when I have a clearer idea
### about the design.

using module .\Utils.psm1
. $PSScriptRoot\header.ps1

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

### Ad-hoc template format used to create default manifests in the following 2 functions.
function RenderTemplate($SrcPath, $DestinationPath, [Hashtable]$TemplateData) {
	$Template = Get-Content -Raw $SrcPath
	foreach ($Entry in $TemplateData.GetEnumerator()) {
		$Template = $Template.Replace("'{{$($Entry.Key)}}'", "'" + $Entry.Value.Replace("'", "''") + "'")
	}
	Set-Content $DestinationPath -Value $Template -NoNewline
}

# TODO: support creating new versions of existing packages (either create a blank package, or copy latest version and modify the Version field);
#  also support automatically retrieving the hash and patching the manifest; ideally, for templated packages in the default form
#  (templated Version + Hash), dev should be able to just call `New-PogPackage 7zip 30.01` and get a finished package without any further tweaking
function New-PogRepositoryPackage {
	### .SYNOPSIS
	### Create a new manifest in the configured package repository.
	### Only supported for local repositories.
	[CmdletBinding()]
	[OutputType([Pog.LocalRepositoryPackage])]
	param(
			### Name of the new manifest. No manifest under that package name should exist.
			[Parameter(Mandatory)]
			[Pog.Verify+PackageName()]
			[string]
		$PackageName,
			### Version of the new manifest.
			[Parameter(Mandatory)]
			[Pog.PackageVersion]
		$Version,
			### Create a templated package.
			[switch]
		$Templated
	)

	begin {
		if ([Pog.InternalState]::Repository -isnot [Pog.LocalRepository]) {
			throw ("Creating new packages is only supported for local repositories, not remote. To add a package to a remote repository, " +`
				"create it in a local repository and then add it to the remote repository (typically with a Git push / PR).")
		}

		$c = [Pog.InternalState]::Repository.GetPackage($PackageName, $true, $false)

		if ($c.Exists) {
            throw "Package '$($c.PackageName)' already exists in the repository at '$($c.Path)'.'"
        }

		$null = New-Item -Type Directory $c.Path
        if ($Templated) {
			$null = New-Item -Type Directory $c.TemplateDirPath
        }

		# only get the package after the parent is created, otherwise it would always default to a non-templated package
		$p = $c.GetVersionPackage($Version, $false)

		$TemplateData = @{NAME = $p.PackageName; VERSION = $p.Version.ToString()}
		if ($Templated) {
			# template dir is already created above
			RenderTemplate "$PSScriptRoot\resources\manifest_templates\repository_templated.psd1" $p.TemplatePath $TemplateData
			RenderTemplate "$PSScriptRoot\resources\manifest_templates\repository_templated_data.psd1" $p.ManifestPath $TemplateData
		} else {
			# create manifest dir for version
			$null = New-Item -Type Directory $p.Path
			RenderTemplate "$PSScriptRoot\resources\manifest_templates\repository_direct.psd1" $p.ManifestPath $TemplateData
		}

		return $p
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
		RenderTemplate "$PSScriptRoot\resources\manifest_templates\imported.psd1" $p.ManifestPath @{NAME = $p.PackageName}

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
		$ListOnly,
			[switch]
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
		# -Force because user already confirmed the update
		$SelectedPackages | pog -Force
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
		foreach ($pn in $PackageName) {
			$Result = $Entries | ListEntries {$_.PackageName -eq $pn}
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