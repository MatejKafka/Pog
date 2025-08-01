﻿using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.PSAttributes;

namespace Pog.Commands;

/// <summary>Lists installed packages.</summary>
/// <para>
/// The `Get-Pog` cmdlet lists installed packages. Each package is represented by a single `Pog.ImportedPackage` instance.
/// By default, packages from all package roots are returned, unless the `-PackageRoot` parameter is set.
/// </para>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "Pog")]
[OutputType(typeof(ImportedPackage))]
public sealed class GetPogCommand : PogCmdlet {
    /// Names of installed packages to return. If not passed, all installed packages are returned.
    [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [ArgumentCompleter(typeof(ImportedPackageNameCompleter))]
    public string[]? PackageName;

    /// Path to the package root in which to list packages. If not passed, matching packages in all package roots are returned.
    [Parameter(ValueFromPipelineByPropertyName = true)]
    [ArgumentCompleter(typeof(ValidPackageRootPathCompleter))]
    public string? PackageRoot;

    private readonly ImportedPackageManager _packages = InternalState.ImportedPackageManager;

    protected override void ProcessRecord() {
        base.ProcessRecord();

        if (PackageRoot != null) {
            // allow passing relative paths to package roots
            PackageRoot = GetUnresolvedProviderPathFromPSPath(PackageRoot);
            try {
                PackageRoot = _packages.ResolveValidPackageRoot(PackageRoot);
            } catch (InvalidPackageRootException e) {
                ThrowTerminatingError(e, "InvalidPackageRoot", ErrorCategory.InvalidArgument, PackageRoot);
            }
        }

        // do not eagerly load the manifest
        if (PackageName == null || !PackageName.Any()) {
            WriteObjectEnumerable(_packages.Enumerate(PackageRoot, false));
        } else {
            foreach (var pn in PackageName) {
                if (WildcardPattern.ContainsWildcardCharacters(pn)) {
                    WriteObjectEnumerable(_packages.Enumerate(PackageRoot, false, pn));
                } else {
                    try {
                        WriteObject(_packages.GetPackage(pn, PackageRoot, true, false));
                    } catch (ImportedPackageNotFoundException e) {
                        WriteError(e, "PackageNotFound", ErrorCategory.ObjectNotFound, pn);
                    } catch (InvalidPackageNameException e) {
                        WriteError(e, "InvalidPackageName", ErrorCategory.InvalidArgument, pn);
                    }
                }
            }
        }
    }
}
