using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.PSAttributes;

namespace Pog.Commands.Common;

[PublicAPI]
public abstract class ImportCommandBase : PackageCommandBase {
    #region Parameter Sets

    // the parameter sets on this command are a bit wild, but essentially, the command needs 2 pieces of information:
    // 1) which repository (source) package to import
    // 2) where (destination) to import it (if not passed, it is inferred from 1))
    //
    // - both packages can be specified by either passing a resolved package, or by specifying a name/version, or a combination
    // - if destination is not specified, we use the same package name as the source and the default package root

    // TODO: explore whether ArgumentTransformation attribute would work better than the parameter sets
    //  (https://vexx32.github.io/2018/12/13/Working-Argument-Transformations/)

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
    protected const string PackageName_PS = "PackageName_"; // if changing this, also change `Invoke-Pog`
    // checks: if any target args or Version are passed, PackageName.Length == 1 and it must not be passed from pipeline
    // Import-Pog -PackageName[] [-Version] -Target
    private const string PackageName_Target_PS = "PackageName_Target";

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
    }

    private static readonly Dictionary<string, PS> _parameterSetMap = new() {
        {Package_TargetName_PS, PS.Package_TargetName_PS},
        {Package_Target_PS, PS.Package_Target_PS},
        {PackageName_TargetName_PS, PS.PackageName_TargetName_PS},
        {PackageName_PS, PS.PackageName_PS},
        {PackageName_Target_PS, PS.PackageName_Target_PS},
    };
    // ReSharper restore InconsistentNaming

    #endregion

    #region Repository Package Arguments

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = Package_TargetName_PS, ValueFromPipeline = true)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = Package_Target_PS)]
    public RepositoryPackage[] Package = null!;

    /// Names of the repository packages to import.
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageName_TargetName_PS, ValueFromPipeline = true)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageName_Target_PS)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageName_PS)]
    [ArgumentCompleter(typeof(RepositoryPackageNameCompleter))]
    public string[] PackageName = null!;

    /// Specific version of the package to import. By default, the latest version is imported.
    [Parameter(Position = 1, ParameterSetName = PackageName_Target_PS)]
    [Parameter(Position = 1, ParameterSetName = PackageName_TargetName_PS)]
    [Parameter(Position = 1, ParameterSetName = PackageName_PS)]
    [ArgumentCompleter(typeof(RepositoryPackageVersionCompleter))]
    public PackageVersion? Version;

    #endregion

    #region Imported Package Arguments

    [Parameter(Mandatory = true, ParameterSetName = Package_Target_PS)]
    [Parameter(Mandatory = true, ParameterSetName = PackageName_Target_PS)]
    public ImportedPackage Target = null!;

    /// Name of the imported package. By default, this is the same as the repository package name.
    /// Use this parameter to distinguish multiple installations of the same package.
    [Parameter(ParameterSetName = Package_TargetName_PS)]
    [Parameter(ParameterSetName = PackageName_TargetName_PS)]
    [ArgumentCompleter(typeof(ImportedPackageNameCompleter))]
    public string? TargetName;

    /// Path to a registered package root, where the package should be imported.
    /// If not set, the default (first) package root is used.
    [Parameter(ParameterSetName = Package_TargetName_PS)]
    [Parameter(ParameterSetName = PackageName_TargetName_PS)]
    [ArgumentCompleter(typeof(ValidPackageRootPathCompleter))]
    public string? TargetPackageRoot;

    #endregion

    /// Overwrite an existing package without prompting for confirmation.
    [Parameter] public SwitchParameter Force;

    /// Show a diff from the previous imported manifest, if any.
    [Parameter] public SwitchParameter Diff;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        if (TargetPackageRoot != null) {
            // allow passing relative paths to package roots
            TargetPackageRoot = GetUnresolvedProviderPathFromPSPath(TargetPackageRoot);
            try {
                TargetPackageRoot = InternalState.ImportedPackageManager.ResolveValidPackageRoot(TargetPackageRoot);
            } catch (InvalidPackageRootException e) {
                ThrowTerminatingError(e, "InvalidTargetPackageRoot", ErrorCategory.InvalidArgument, TargetPackageRoot);
            }
        }

        if (TargetName != null && MyInvocation.ExpectingInput) {
            ThrowArgumentError(TargetName, "TargetWithPipelineInput",
                    "-TargetName must not be passed together with pipeline input.");
        }

        if (Version != null && MyInvocation.ExpectingInput) {
            ThrowArgumentError(Version, "VersionWithPipelineInput",
                    "-Version must not be passed together with pipeline input.");
        }

        if (Version != null && PackageName.Length > 1) {
            ThrowArgumentError(Version, "VersionWithMultiplePackages",
                    "-Version must not be passed when -PackageName contains multiple package names.");
        }
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();
        var parameterSet = _parameterSetMap[ParameterSetName];
        var srcParameterSet = parameterSet & (PS.Package | PS.PackageName);
        var targetParameterSet = parameterSet & (PS.Target | PS.TargetName);

        var rawSrcCount = srcParameterSet switch {
            PS.Package => Package.Length,
            PS.PackageName => PackageName.Length,
            _ => throw new ArgumentOutOfRangeException(),
        };

        var hasTarget = targetParameterSet switch {
            PS.Target => true,
            PS.TargetName => TargetName != null,
            _ => throw new ArgumentOutOfRangeException(),
        };

        // prevent overwriting a single target with multiple source packages
        if (rawSrcCount > 1 && hasTarget) {
            ThrowArgumentError(null, "MultipleSourcesForSingleTarget",
                    $"Exactly one source package must be specified when an explicit target is specified, got {rawSrcCount}.");
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
            PS.Target => Target,
            // target does not need to exist here
            PS.TargetName => TargetName == null ? null : GetTargetPackage(TargetName, TargetPackageRoot),
            _ => throw new ArgumentOutOfRangeException(),
        };

        Debug.Assert(srcPackages.Length == 1 || targetPackage == null);

        if (targetPackage != null) {
            ProcessPackage(srcPackages[0], targetPackage);
        } else {
            foreach (var src in srcPackages) {
                ProcessPackage(src, GetTargetPackage(src.PackageName, TargetPackageRoot));
            }
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

    protected abstract void ProcessPackage(RepositoryPackage source, ImportedPackage target);
}
