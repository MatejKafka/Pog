using module .\Paths.psm1
using module .\lib\Utils.psm1
using module .\Confirmations.psm1
. $PSScriptRoot\lib\header.ps1

# re-export binary cmdlets from Pog.dll
Export-ModuleMember -Cmdlet `
	Import-Pog, Install-Pog, Enable-Pog, Export-Pog, Disable-Pog, Uninstall-Pog, `
	Get-PogPackage, Get-PogRepositoryPackage, Get-PogRoot, `
	Confirm-PogPackage, Confirm-PogRepositoryPackage, `
	Clear-PogDownloadCache, Show-PogManifestHash


# functions to programmatically add/remove package roots are intentionally not provided, because it is a bit non-trivial
#  to get the file updates right from a concurrency perspective
# TODO: ^ figure out how to provide the functions safely
Export function Edit-PogRootList {
	$Path = $PACKAGE_ROOTS.PackageRoots.PackageRootFile
	Write-Information "Opening the package root list at '$Path' for editing in a text editor..."
	Write-Information "Each line should contain a single absolute path to the package root directory."
	# this opens the file for editing in a text editor (it's a .txt file)
	Start-Process $Path
}

# defined below
Export alias pog Invoke-Pog

# CmdletBinding is manually copied from Import-Pog, there doesn't seem any way to dynamically copy this like with dynamicparam
# TODO: rollback on error
# TODO: allow wildcards in PackageName and Version arguments for commands where it makes sense
Export function Invoke-Pog {
	# .SYNOPSIS
	#   Import, install, enable and export a package.
	# .DESCRIPTION
	#	Runs all four installation stages in order. All arguments passed to this cmdlet,
	#	except for the `-InstallOnly` switch, are forwarded to `Import-Pog`.
	[CmdletBinding(PositionalBinding = $false, DefaultParameterSetName = "PackageName_")]
	param(
			### Only import and install the package, do not enable and export.
			[switch]
		$InstallOnly,
			### Import, install and enable the package, do not export it.
			[switch]
		$NoExport
	)

	dynamicparam {
		$ParamBuilder = [Pog.Commands.Common.DynamicCommandParameters+Builder]::new("", "None", {
			param($ParamName, $Attr)
			throw "Cannot copy parameter '$ParamName', attribute '$($Attr.GetType().ToString())'."
		})
		$CopiedParams = $ParamBuilder.CopyParameters((Get-Command Import-Pog))
		return $CopiedParams
	}

	begin {
		$Params = $CopiedParams.Extract()

		# reuse PassThru parameter from Import-Pog for Enable-Pog
		$PassThru = [bool]$Params["PassThru"]

		$LogArgs = @{}
		if ($PSBoundParameters.ContainsKey("Verbose")) {$LogArgs["Verbose"] = $PSBoundParameters.Verbose}
		if ($PSBoundParameters.ContainsKey("Debug")) {$LogArgs["Debug"] = $PSBoundParameters.Debug}

		$null = $Params.Remove("PassThru")

		$SbAll =      {Import-Pog -PassThru @Params | Install-Pog -PassThru @LogArgs | Enable-Pog -PassThru @LogArgs | Export-Pog -PassThru:$PassThru @LogArgs}
		$SbNoExport = {Import-Pog -PassThru @Params | Install-Pog -PassThru @LogArgs | Enable-Pog -PassThru:$PassThru @LogArgs}
		$SbNoEnable = {Import-Pog -PassThru @Params | Install-Pog -PassThru:$PassThru @LogArgs}

		$Sb = if ($InstallOnly) {$SbNoEnable}
			elseif ($NoExport) {$SbNoExport}
			else {$SbAll}

		$sp = $Sb.GetSteppablePipeline()
		$sp.Begin($PSCmdlet)
	}

	process {
		$sp.Process($_)
	}

	end {
		$sp.End()
	}
}


<# Ad-hoc template format used to create default manifests in the following 2 functions. #>
function RenderTemplate($SrcPath, $DestinationPath, [Hashtable]$TemplateData) {
	$Template = Get-Content -Raw $SrcPath
	foreach ($Entry in $TemplateData.GetEnumerator()) {
		$Template = $Template.Replace("'{{$($Entry.Key)}}'", "'" + $Entry.Value.Replace("'", "''") + "'")
	}
	$null = New-Item -Path $DestinationPath -Value $Template
}

Export function New-PogPackage {
	[CmdletBinding()]
	[OutputType([Pog.RepositoryPackage])]
	param(
			[Parameter(Mandatory)]
			[Pog.Verify+PackageName()]
			[string]
		$PackageName,
			[Parameter(Mandatory)]
			[Pog.PackageVersion]
		$Version,
			[switch]
		$Templated
	)

	begin {
		$c = $REPOSITORY.GetPackage($PackageName, $true, $false)

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

Export function New-PogImportedPackage {
	[CmdletBinding()]
	[OutputType([Pog.ImportedPackage])]
	param(
			[Parameter(Mandatory)]
			[Pog.Verify+PackageName()]
			[string]
		$PackageName,
			[ArgumentCompleter([Pog.PSAttributes.ValidPackageRootPathCompleter])]
			[string]
		$PackageRoot
	)

	begin {
		$PackageRoot = if (-not $PackageRoot) {
			$PACKAGE_ROOTS.DefaultPackageRoot
		} else {
			try {$PACKAGE_ROOTS.ResolveValidPackageRoot($PackageRoot)}
			catch [Pog.InvalidPackageRootException] {
				$PSCmdlet.ThrowTerminatingError($_)
			}
		}

		$p = $PACKAGE_ROOTS.GetPackage($PackageName, $PackageRoot, $false, $false, $false)
		if ($p.Exists) {
			throw "Package already exists: $($p.Path)"
		}

		# create the package dir
		$null = New-Item -Type Directory $p.Path
		RenderTemplate "$PSScriptRoot\resources\manifest_templates\imported.psd1" $p.ManifestPath @{NAME = $p.PackageName}

		return $p
	}
}

<# Retrieve all existing versions of a package by calling the package version generator script. #>
function RetrievePackageVersions([Pog.PackageGenerator]$Generator, $ExistingVersionSet) {
	foreach ($Obj in & $Generator.Manifest.ListVersionsSb $ExistingVersionSet) {
		# the returned object should either be the version string directly, or a map object
		#  (hashtable/pscustomobject/psobject/ordered) that has the Version property
		#  why not use -is? that's why: https://github.com/PowerShell/PowerShell/issues/16361
		$IsMap = $Obj.PSTypeNames[0] -in @("System.Collections.Hashtable", "System.Management.Automation.PSCustomObject", "System.Collections.Specialized.OrderedDictionary")
		$VersionStr = if (-not $IsMap) {$Obj} else {
			try {$Obj.Version}
			catch {
				throw "Version generator for package '$($Generator.PackageName)' returned a custom object without a Version property: $Obj" +`
					"  (Version generators must return either a version string, or a" +`
					" map container (hashtable, psobject, pscustomobject) with a Version property.)"
			}
		}

		if ([string]::IsNullOrEmpty($VersionStr)) {
			throw "Empty package version generated by the version generator for package '$($Generator.PackageName)' (either `$null or empty string)."
		}

		[pscustomobject]@{
			Version = [Pog.PackageVersion]$VersionStr
			# store the original value, so that we can pass it unchanged to the manifest generator
			OriginalValue = $Obj
		}
	}
}

function UpdateSinglePackage([string]$PackageName, [string[]]$Version,  [switch]$Force, [switch]$ListOnly) {
	$g = try {$GENERATOR_REPOSITORY.GetPackage($PackageName, $true, $true)}
		catch [Pog.PackageGeneratorNotFoundException] {throw $_}

	$c = try {$REPOSITORY.GetPackage($PackageName, $true, $true)}
		catch [Pog.RepositoryPackageNotFoundException] {throw $_}

	if (-not $c.IsTemplated) {
		throw "Package '$($c.PackageName)' $(if ($c.Exists) {"is not templated"} else {"does not exist yet"}), " +`
			"manifest generators are only supported for existing templated packages."
	}

	# list available versions without existing manifest (unless -Force is set, then all versions are listed)
	# only generate manifests for versions that don't already exist, unless -Force is passed
	$ExistingVersions = [System.Collections.Generic.HashSet[string]]::new($c.EnumerateVersionStrings())
	$GeneratedVersions = RetrievePackageVersions $g $ExistingVersions `
		<# if -Force was not passed, filter out versions with already existing manifest #> `
		| ? {$Force -or -not $ExistingVersions.Contains($_.Version)} `
		<# if $Version was passed, filter out the versions; as the versions generated by the script
		   may have other metadata, we cannot use the versions passed in $Version directly #> `
		| ? {-not $Version -or $_.Version -in $Version}

	if ($Version -and @($Version).Count -ne @($GeneratedVersions).Count) {
		$FoundVersions = $GeneratedVersions | % {$_.Version}
		$MissingVersionsStr = ($Version | ? {$_ -notin $FoundVersions}) -join ", "
		throw "Some of the package versions passed in -Version were not found for package '$($c.PackageName)': $MissingVersionsStr " +`
			"(Are you sure these versions exist?)"
		return
	}

	if ($ListOnly) {
		# useful for testing if all expected versions are retrieved
		return $GeneratedVersions | % {$c.GetVersionPackage($_.Version, $false)}
	}

	# generate manifest for each version
	foreach ($v in $GeneratedVersions) {
		$p = $c.GetVersionPackage($v.Version, $false)

		$TemplateData = if ($g.Manifest.GenerateSb) {
			# pass the value both as $_ and as a parameter, the scriptblock can accept whichever one is more convenient
			Invoke-DollarUnder $g.Manifest.GenerateSb $v.OriginalValue $v.OriginalValue
		} else {
			$v.OriginalValue # if no Generate block exists, forward the value emitted by ListVersions
		}

		$Count = @($TemplateData).Count
		if ($Count -ne 1) {
			throw "Manifest generator for package '$($p.PackageName)' generated " +`
				"$(if ($Count -eq 0) {"no output"} else {"multiple values"}) for version '$($p.Version)', expected a single [Hashtable]."
		}

		# unwrap the collection
		$TemplateData = @($TemplateData)[0]

		if ($TemplateData -isnot [Hashtable] -and $TemplateData -isnot [System.Collections.Specialized.OrderedDictionary]) {
			$Type = if ($TemplateData) {$TemplateData.GetType().ToString()} else {"null"}
			throw "Manifest generator for package '$($p.PackageName)' did not generate a [Hashtable] for version '$($p.Version)', got '$Type'."
		}

		# write out the manifest
		[Pog.ManifestTemplateFile]::SerializeSubstitutionFile($p.ManifestPath, $TemplateData)

		# manifest is not loaded yet, no need to reload
		echo $p
	}
}

# TODO: run this inside a container
# FIXME: if -Force is passed, track if there are any leftover manifests (for removed versions) and delete them
Export function Update-PogManifest {
	# .SYNOPSIS
	#	Generate new manifests in the package repository for the given package manifest generator.
	[CmdletBinding()]
	[OutputType([Pog.RepositoryPackage])]
	param(
			### Name of the manifest generator for which to generate new manifests.
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
		$ListOnly
	)

	begin {
		if ($Version) {
			if ($MyInvocation.ExpectingInput) {throw "-Version must not be passed together with pipeline input."}
			if (-not $PackageName) {throw "-Version must not be passed without also passing -PackageName."}
			if (@($PackageName).Count -gt 1) {throw "-Version must not be passed when -PackageName contains multiple package names."}
		}

		$ShowProgressBar = $false
		# by default, return all available packages
		if (-not $PSBoundParameters.ContainsKey("PackageName") -and -not $MyInvocation.ExpectingInput) {
			$PackageName = $GENERATOR_REPOSITORY.EnumerateGeneratorNames()
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
				UpdateSinglePackage $pn $Version -Force:$Force -ListOnly:$ListOnly
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