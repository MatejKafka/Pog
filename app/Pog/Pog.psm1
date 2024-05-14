using module .\lib\Utils.psm1
. $PSScriptRoot\lib\header.ps1

# if there are any missing package roots, show a warning
foreach ($r in [Pog.InternalState]::PathConfig.PackageRoots.MissingPackageRoots) {
	Write-Warning ("Could not find package root '$r'. Create the directory, or remove it" `
			+ " from the package root list using the 'Edit-PogRootList' command.")
}


# re-export binary cmdlets from Pog.dll
Export-ModuleMember -Alias pog -Cmdlet `
	Invoke-Pog, Import-Pog, Install-Pog, Enable-Pog, Export-Pog, Disable-Pog, Uninstall-Pog, `
	Get-PogPackage, Get-PogRepositoryPackage, Get-PogRoot, `
	Confirm-PogPackage, Confirm-PogRepositoryPackage, `
	Clear-PogDownloadCache, Show-PogManifestHash


Export function Set-PogRepository {
	### .SYNOPSIS
	### 	Selects the package repository used by other commands. Not thread-safe.
	[CmdletBinding(DefaultParameterSetName="Local")]
	param(
			### URI of a remote repository to use.
			[Parameter(Mandatory, ParameterSetName="Remote", Position=0)]
			[Uri]
		$Uri,
			### Path to a local repository to use.
			[Parameter(Mandatory, ParameterSetName="Local", Position=0)]
			[string]
		$Path
	)

	begin {
		if ($Path) {
			$null = [Pog.InternalState]::SetRepository([Pog.LocalRepository]::new((Resolve-Path $Path)))
			Write-Information "Using a local repository: $([Pog.InternalState]::Repository.Path)"
		} else {
			$null = [Pog.InternalState]::SetRepository([Pog.RemoteRepository]::new($Uri))
			Write-Information "Using a remote repository: $([Pog.InternalState]::Repository.Url)"
		}
	}
}

# functions to programmatically add/remove package roots are intentionally not provided, because it is a bit non-trivial
#  to get the file updates right from a concurrency perspective
# TODO: ^ figure out how to provide the functions safely
Export function Edit-PogRootList {
	### .SYNOPSIS
	### 	Opens the configuration file listing package roots in a text editor.
	[CmdletBinding()]
	param()

	$Path = [Pog.InternalState]::ImportedPackageManager.PackageRoots.PackageRootFile
	Write-Information "Opening the package root list at '$Path' for editing in a text editor..."
	Write-Information "Each line should contain a single absolute path to the package root directory."
	# this opens the file for editing in a text editor (it's a .txt file)
	Start-Process $Path
}

<# Ad-hoc template format used to create default manifests in the following 2 functions. #>
function RenderTemplate($SrcPath, $DestinationPath, [Hashtable]$TemplateData) {
	$Template = Get-Content -Raw $SrcPath
	foreach ($Entry in $TemplateData.GetEnumerator()) {
		$Template = $Template.Replace("'{{$($Entry.Key)}}'", "'" + $Entry.Value.Replace("'", "''") + "'")
	}
	$null = New-Item -Path $DestinationPath -Value $Template
}

# TODO: support creating new versions of existing packages (either create a blank package, or copy latest version and modify the Version field);
#  also support automatically retrieving the hash and patching the manifest; ideally, for templated packages in the default form
#  (templated Version + Hash), dev should be able to just call `New-PogPackage 7zip 30.01` and get a finished package without any further tweaking
Export function New-PogRepositoryPackage {
	### .SYNOPSIS
	### 	Create a new manifest in the configured package repository.
	### 	Only supported for local repositories.
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
			throw "Creating new packages is only supported for local repositories, not remote."
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

Export function New-PogPackage {
	### .SYNOPSIS
	### 	Creates a new empty package directory with a default manifest.
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


function UpdateSinglePackage([string]$PackageName, [string[]]$Version, [switch]$Force, [switch]$ListOnly, $GitHubToken) {
	Write-Information "Checking updates for '$PackageName'..."

	$g = try {[Pog.InternalState]::GeneratorRepository.GetPackage($PackageName, $true, $true)}
		catch [Pog.PackageGeneratorNotFoundException] {throw $_}

	$null = $g.ReloadManifest()

	$c = try {[Pog.InternalState]::Repository.GetPackage($PackageName, $true, $true)}
		catch [Pog.RepositoryPackageNotFoundException] {throw $_}

	if (-not $c.IsTemplated) {
		throw "Package '$($c.PackageName)' $(if ($c.Exists) {"is not templated"} else {"does not exist yet"}), " +`
			"manifest generators are only supported for existing templated packages."
	}

	Invoke-Container -Modules $PSScriptRoot\container\Env_ManifestGenerator.psm1 -ArgumentList @($g, $c, $Version, $Force, $ListOnly, $GitHubToken)
}

Export function Update-PogManifest {
	### .SYNOPSIS
	### 	Generate new manifests in a local package repository for the selected package manifest generator.
	[CmdletBinding()]
	[OutputType([Pog.LocalRepositoryPackage])]
	param(
			### Name of the manifest generator for which to generate new manifests.
			### If not passed, all existing generators are invoked.
			[Parameter(ValueFromPipeline, ValueFromPipelineByPropertyName)]
			[ArgumentCompleter([Pog.PSAttributes.RepositoryPackageGeneratorNameCompleter])]
			[string[]]
		$PackageName,
			# we only use -Version to match against retrieved versions, no need to parse
			### List of versions to generate/update manifests for.
			[Parameter(ValueFromPipelineByPropertyName)]
			[string[]]
		$Version,
			### Regenerate even existing manifests. By default, only manifests for versions that
			### do not currently exist in the repository are generated.
			[switch]
		$Force,
			### Only retrieve and list versions, do not generate manifests.
			[switch]
		$ListOnly,
			### GitHuh access token, automatically used by the provided cmdlets communicating with GitHub.
			### One possible use case is increasing the API rate limit, which is quite low for unauthenticated callers.
			[securestring]
		$GitHubToken
	)

	begin {
		if ([Pog.InternalState]::Repository -isnot [Pog.LocalRepository]) {
			throw "Generating new packages is only supported for local repositories, not remote."
		}

		if ($Version) {
			if ($MyInvocation.ExpectingInput) {throw "-Version must not be passed together with pipeline input."}
			if (-not $PackageName) {throw "-Version must not be passed without also passing -PackageName."}
			if (@($PackageName).Count -gt 1) {throw "-Version must not be passed when -PackageName contains multiple package names."}
		}

		$ShowProgressBar = $false
		# by default, return all available packages
		if (-not $PSBoundParameters.ContainsKey("PackageName") -and -not $MyInvocation.ExpectingInput) {
			$PackageName = [Pog.InternalState]::GeneratorRepository.EnumerateGeneratorNames()
			$ShowProgressBar = $true
		}

		if ($Version) {
			# if -Version was passed, overwrite even existing manifests
			$Force = $true
		}
	}

	process {
		if ($ShowProgressBar) {
			$i = 0
			$pnCount = @($PackageName).Count
		}

		foreach ($pn in $PackageName) {
			if ($ShowProgressBar) {
				Write-Progress -Activity "Updating manifests" -PercentComplete (100 * ($i/$pnCount)) -Status "$($i+1)/${pnCount} Updating '$pn'..."
				$i++
			}

			try {
				UpdateSinglePackage $pn $Version -Force:$Force -ListOnly:$ListOnly -GitHubToken:$GitHubToken
			} catch {& {
				$ErrorActionPreference = "Continue"
				$PSCmdlet.WriteError($_)
			}}
		}
	}

	end {
		if ($ShowProgressBar) {
			Write-Progress -Activity "Updating manifests" -Completed
		}
	}
}