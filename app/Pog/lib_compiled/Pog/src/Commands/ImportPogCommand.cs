﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using JetBrains.Annotations;
using Pog.Commands.Common;

namespace Pog.Commands;

/// <summary>
/// <para type="synopsis">Imports a package manifest from the repository.</para>
/// <para type="description">
/// Imports a package from the repository by copying the package manifest to the target path.
/// To fully install a package (equivalent to e.g. `winget install`), use the `pog` command, or run the following stages
/// of installation manually by invoking `Install-Pog`, `Enable-Pog` and `Export-Pog`.
/// </para>
/// </summary>
[PublicAPI]
[Cmdlet(VerbsData.Import, "Pog", DefaultParameterSetName = PackageName_PS, SupportsShouldProcess = true)]
[OutputType(typeof(ImportedPackage))]
public sealed class ImportPogCommand : PackageCommandBase {
    #region Parameter Sets

    // the package sets on this command are a bit wild, but essentially, the command needs 2 pieces of information:
    // 1) which repository (source) package to import (if not passed, it is inferred from 2))
    // 2) where (destination) to import it (if not passed, it is inferred from 1))
    //
    // - both packages can be specified by either passing a resolved package, or by specifying a name/version, or a combination
    // - if source is not specified, we take the latest package matching the ManifestName of the target (auto-update)
    // - if destination is not specified, we use the same package name as the source and the default package root

    // ReSharper disable InconsistentNaming
    // checks: if any target args are passed, Package.Length == 1 and it must not be passed from pipeline
    // Import-Pog -Package[] [-TargetName] [-TargetPackageRoot] // -Package from pipeline
    private const string Package_TargetName_PS = "Package_TargetName";
    // checks: if any target args are passed, Package.Length == 1 and it must not be passed from pipeline
    // Import-Pog -Package[] -Target
    private const string Package_Target_PS = "Package_Target";

    // checks: if any target args or Version are passed, PackageName.Length == 1 and it must not be passed from pipeline
    // Import-Pog -PackageName[] [-Version] [-TargetName] [-TargetPackageRoot] // -PackageName from pipeline, default
    private const string PackageName_TargetName_PS = "PackageName_TargetName";
    // this parameter set is redundant with the previous one, but it resolves ambiguity in `Import-Pog -TargetName <...>`,
    //  which would be treated as the `PackageName_TargetName_PS` if it was the default parameter set
    private const string PackageName_PS = "PackageName_"; // if changing this, also change `Invoke-Pog`
    // checks: if any target args or Version are passed, PackageName.Length == 1 and it must not be passed from pipeline
    // Import-Pog -PackageName[] [-Version] -Target
    private const string PackageName_Target_PS = "PackageName_Target";

    // Import-Pog -Target
    private const string Target_PS = "_Target";
    // checks: standard GetPackage (must exist)
    // Import-Pog -TargetName [-TargetPackageRoot]
    private const string TargetName_PS = "_TargetName";

    internal const string DefaultPS = PackageName_PS;

    // split parameter set into flags for each possible value
    [Flags]
    private enum PS {
        Package = 1,
        PackageName = 2,
        Target = 4,
        TargetName = 8,

        Package_TargetName_PS = Package | TargetName,
        Package_Target_PS = Package | Target,
        PackageName_TargetName_PS = PackageName | TargetName,
        PackageName_PS = PackageName_TargetName_PS,
        PackageName_Target_PS = PackageName | Target,
        Target_PS = Target,
        TargetName_PS = TargetName,
    }

    private static readonly Dictionary<string, PS> _parameterSetMap = new() {
        {Package_TargetName_PS, PS.Package_TargetName_PS},
        {Package_Target_PS, PS.Package_Target_PS},
        {PackageName_TargetName_PS, PS.PackageName_TargetName_PS},
        {PackageName_PS, PS.PackageName_PS},
        {PackageName_Target_PS, PS.PackageName_Target_PS},
        {Target_PS, PS.Target_PS},
        {TargetName_PS, PS.TargetName_PS},
    };
    // ReSharper restore InconsistentNaming

    #endregion

    #region Repository Package Arguments

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = Package_TargetName_PS, ValueFromPipeline = true)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = Package_Target_PS)]
    public RepositoryPackage[] Package = null!;

    /// <summary><para type="description">
    /// Names of the repository packages to import.
    /// </para></summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageName_TargetName_PS, ValueFromPipeline = true)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageName_Target_PS)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageName_PS)]
    [ArgumentCompleter(typeof(PSAttributes.RepositoryPackageNameCompleter))]
    public string[] PackageName = null!;

    /// <summary><para type="description">
    /// Specific version of the package to import. By default, the latest version is imported.
    /// </para></summary>
    [Parameter(Position = 1, ParameterSetName = PackageName_Target_PS)]
    [Parameter(Position = 1, ParameterSetName = PackageName_TargetName_PS)]
    [Parameter(Position = 1, ParameterSetName = PackageName_PS)]
    [ArgumentCompleter(typeof(PSAttributes.RepositoryPackageVersionCompleter))]
    public PackageVersion? Version;

    #endregion

    #region Imported Package Arguments

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = Target_PS, ValueFromPipeline = true)]
    [Parameter(Mandatory = true, Position = 1, ParameterSetName = Package_Target_PS)]
    [Parameter(Mandatory = true, ParameterSetName = PackageName_Target_PS)]
    public ImportedPackage[] Target = null!;

    /// <summary><para type="description">
    /// Name of the imported package. By default, this is the same as the repository package name.
    /// Use this parameter to distinguish multiple installations of the same package.
    /// </para></summary>
    [Parameter(Mandatory = true, ParameterSetName = TargetName_PS)]
    [Parameter(ParameterSetName = Package_TargetName_PS)]
    [Parameter(ParameterSetName = PackageName_TargetName_PS)]
    [ArgumentCompleter(typeof(PSAttributes.ImportedPackageNameCompleter))]
    public string[]? TargetName;

    /// <summary><para type="description">
    /// Path to a registered package root, where the package should be imported.
    /// If not set, the default (first) package root is used.
    /// </para></summary>
    [Parameter(ParameterSetName = TargetName_PS)]
    [Parameter(ParameterSetName = Package_TargetName_PS)]
    [Parameter(ParameterSetName = PackageName_TargetName_PS)]
    [ArgumentCompleter(typeof(PSAttributes.ValidPackageRootPathCompleter))]
    public string? TargetPackageRoot;

    #endregion

    /// <summary><para type="description">
    /// Overwrite an existing package without prompting for confirmation.
    /// </para></summary>
    [Parameter] public SwitchParameter Force;

    /// <summary><para type="description">
    /// Return a [Pog.ImportedPackage] object with information about the imported package.
    /// </para></summary>
    [Parameter] public SwitchParameter PassThru;

    /// <summary><para type="description">
    /// Show a diff from the previous imported manifest.
    /// </para></summary>
    [Parameter] public SwitchParameter Diff;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        if (TargetPackageRoot != null) {
            try {
                TargetPackageRoot = InternalState.ImportedPackageManager.ResolveValidPackageRoot(TargetPackageRoot);
            } catch (InvalidPackageRootException e) {
                ThrowTerminatingError(e, "InvalidTargetPackageRoot", ErrorCategory.InvalidArgument, TargetPackageRoot);
            }
        }

        if (TargetName != null && MyInvocation.ExpectingInput) {
            ThrowTerminatingArgumentError(TargetName, "TargetWithPipelineInput",
                    "-TargetName must not be passed together with pipeline input.");
        }

        if (Version != null && MyInvocation.ExpectingInput) {
            ThrowTerminatingArgumentError(Version, "VersionWithPipelineInput",
                    "-Version must not be passed together with pipeline input.");
        }

        if (Version != null && PackageName.Length > 1) {
            ThrowTerminatingArgumentError(Version, "VersionWithMultiplePackages",
                    "-Version must not be passed when -PackageName contains multiple package names.");
        }
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();
        var parameterSet = _parameterSetMap[ParameterSetName];

        // only target is specified, update it to the latest version
        if (parameterSet is PS.Target or PS.TargetName) {
            var targetPackages = parameterSet == PS.Target
                    ? Target
                    : GetImportedPackage(TargetName!, TargetPackageRoot, true);
            foreach (var target in targetPackages) {
                UpdateImportedPackage(target);
            }
            return;
        }

        var srcParameterSet = parameterSet & (PS.Package | PS.PackageName);
        var targetParameterSet = parameterSet & (PS.Target | PS.TargetName);

        var rawSrcCount = srcParameterSet switch {
            PS.Package => Package.Length,
            PS.PackageName => PackageName.Length,
            _ => (int?) null,
        };
        var rawTargetCount = targetParameterSet switch {
            PS.Target => Target.Length,
            PS.TargetName => TargetName?.Length,
            _ => null,
        };

        // if TargetName or Target are an array, throw an error (not allowed when source is set)
        if (rawTargetCount > 1) {
            ThrowTerminatingArgumentError(null, "MultipleTargets",
                    "At most one target must be specified when a source package is provided to avoid ambiguity.");
        }
        // prevent overwriting a single target with multiple source packages
        if (rawSrcCount > 1 && rawTargetCount != null) {
            ThrowTerminatingArgumentError(null, "MultipleSourcesForSingleTarget",
                    "At most one source package must be specified when an explicit target is specified.");
        }

        var srcPackages = srcParameterSet switch {
            PS.Package => Package,
            PS.PackageName => GetRepositoryPackage(PackageName, Version).ToArray(),
            _ => throw new ArgumentOutOfRangeException(),
        };

        if (srcPackages.Length == 0) {
            return; // no valid source packages, nothing to do
        }

        var targetPackage = targetParameterSet switch {
            PS.Target => Target[0],
            // target does not need to exist here
            PS.TargetName => TargetName == null ? null : GetTargetPackage(TargetName[0], TargetPackageRoot),
            _ => throw new ArgumentOutOfRangeException(),
        };

        Debug.Assert(srcPackages.Length == 1 || targetPackage == null);

        if (targetPackage != null) {
            ImportPackage(srcPackages[0], targetPackage);
        } else {
            foreach (var src in srcPackages) {
                ImportPackage(src, GetTargetPackage(src.PackageName, TargetPackageRoot));
            }
        }
    }

    private void UpdateImportedPackage(ImportedPackage target) {
        var srcName = target.ManifestName;
        if (srcName == null) {
            var e = new RepositoryPackageNotFoundException(
                    $"Cannot update package '{target.PackageName}', its manifest does not contain a package " +
                    "name, so we cannot resolve the source package.");
            WriteError(e, "NoManifestName", ErrorCategory.InvalidOperation, target);
            return;
        }

        var src = GetRepositoryPackage(srcName);
        if (src != null) {
            ImportPackage(src, target);
        }
    }

    // target does not need to exist
    private static ImportedPackage GetTargetPackage(string packageName, string? packageRoot) {
        // don't load the manifest yet (may not be valid, will be loaded in ConfirmManifestOverwrite)
        if (packageRoot != null) {
            return InternalState.ImportedPackageManager.GetPackage(packageName, packageRoot, true, false, false);
        } else {
            return InternalState.ImportedPackageManager.GetPackageDefault(packageName, true, false);
        }
    }

    private void ImportPackage(RepositoryPackage package, ImportedPackage target) {
        // TODO: in the PowerShell version, we used to run Confirm-PogRepositoryManifest here;
        //  think through whether it's a good idea to add that back

        // FIXME: for templated manifests, `MatchesRepositoryManifest` will build the manifest once,
        //  and then .ImportTo will build it a second time; figure out how to avoid the duplication
        if (!Force && package.MatchesImportedManifest(target)) {
            // not happy with this being a warning, but WriteHost is imo not the right one to use here,
            // and WriteInformation will not be visible for users with default $InformationPreference
            WriteWarning($"Skipping import of package '{package.PackageName}', " +
                         "target already contains this package. Pass '-Force' to override.");
            return;
        }

        var actionStr = $"Importing {package.GetDescriptionString()} to '{target.Path}'.";
        if (!ShouldProcess(actionStr, actionStr, null)) {
            return;
        }

        if (!ConfirmManifestOverwrite(package, target)) {
            WriteInformation($"Skipping import of package '{package.PackageName}'.");
            return;
        }

        // import the package, replacing the previous manifest (and creating the directory if the package is new)
        package.ImportTo(target);

        WriteInformation($"Initialized '{target.Path}' with package manifest '{package.PackageName}', " +
                         $"version '{package.Version}'.");

        if (PassThru) {
            WriteObject(target);
        }
    }

    private bool ConfirmManifestOverwrite(RepositoryPackage package, ImportedPackage target) {
        PackageManifest? targetManifest = null;
        try {
            // try to load the (possibly) existing manifest
            // TODO: maybe add a method to only load the name and version from the manifest and skip full validation?
            targetManifest = target.ReloadManifest();
        } catch (PackageNotFoundException) {
            // the package does not exist, no need to confirm
            return true;
        } catch (PackageManifestNotFoundException) {
            // the package exists, but the manifest is missing
            // either a random folder was erroneously created, or this is a package, but corrupted
            WriteWarning($"A package directory already exists at '{target.Path}', but it doesn't seem to contain " +
                         $"a package manifest. All directories in a package root should be packages with a valid manifest.");
            // overwrite without confirmation
            return true;
        } catch (Exception e) when (e is IPackageManifestException) {
            WriteWarning($"Found an existing package manifest at '{target.Path}', but it is not valid.");
        }

        if (Diff && targetManifest != null) {
            // TODO: also probably check if any supporting files in .pog changed
            var diff = InlineDiffBuilder.Diff(targetManifest.RawString, package.Manifest.RawString);
            if (diff.HasDifferences) {
                foreach (var d in diff.Lines) {
                    ConsoleColor? color = d.Type switch {
                        ChangeType.Inserted => ConsoleColor.DarkGreen,
                        ChangeType.Deleted => ConsoleColor.DarkRed,
                        ChangeType.Modified => ConsoleColor.Gray,
                        _ => null,
                    };

                    if (color != null) {
                        WriteHost(d.Text, foregroundColor: color);
                    }
                }
            }
        }

        if (Force) {
            return true;
        }

        if (targetManifest?.Version != null && targetManifest.Version < package.Version) {
            // target is older than the imported package, continue silently
            return true;
        }

        // prompt for confirmation
        var downgrading = targetManifest?.Version != null && targetManifest.Version > package.Version;
        var title = $"{(downgrading ? "Downgrade" : "Overwrite")} an existing package manifest for '{target.PackageName}'?";
        var manifestDescription =
                targetManifest == null ? "" : $" (manifest '{targetManifest.Name}', version '{targetManifest.Version}')";
        var message = $"There is already an imported package '{target.PackageName}' at '{target.Path}'" +
                      $"{manifestDescription}. Overwrite its manifest with version '{package.Version}'?";
        return ShouldContinue(message, title);
    }
}
