using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;

namespace Pog.Commands;

/// <summary>
/// <para type="synopsis">Lists installed packages.</para>
/// <para type="description">
/// The `Get-PogPackage` cmdlet lists installed packages. Each package is represented by a single `Pog.ImportedPackage` instance.
/// By default, packages from all package roots are returned, unless the `-PackageRoot` parameter is set.
/// </para>
/// </summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "PogPackage")]
[OutputType(typeof(ImportedPackage))]
public sealed class GetPogPackageCommand : PogCmdlet {
    /// <summary><para type="description">
    /// Names of installed packages to return. If not passed, all installed packages are returned.
    /// </para></summary>
    [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [ArgumentCompleter(typeof(PSAttributes.ImportedPackageNameCompleter))]
    public string[]? PackageName;

    /// <summary><para type="description">
    /// Path to the content root in which to list packages. If not passed, installed packages from all package roots are returned.
    /// </para></summary>
    [Parameter(ValueFromPipelineByPropertyName = true)]
    [ArgumentCompleter(typeof(PSAttributes.ValidPackageRootPathCompleter))]
    public string? PackageRoot;

    private readonly ImportedPackageManager _packages = InternalState.ImportedPackageManager;

    protected override void ProcessRecord() {
        base.ProcessRecord();

        if (PackageRoot != null) {
            try {
                PackageRoot = _packages.ResolveValidPackageRoot(PackageRoot);
            } catch (InvalidPackageRootException e) {
                ThrowTerminatingError(e, "InvalidPackageRoot", ErrorCategory.InvalidArgument, PackageRoot);
            }
        }

        // do not eagerly load the manifest
        if (PackageName == null) {
            WriteObjectEnumerable(PackageRoot != null
                    ? _packages.EnumeratePackages(PackageRoot, false)
                    : _packages.EnumeratePackages(false));
        } else {
            foreach (var pn in PackageName) {
                try {
                    WriteObject(PackageRoot != null
                            ? _packages.GetPackage(pn, PackageRoot, true, false, true)
                            : _packages.GetPackage(pn, true, false));
                } catch (ImportedPackageNotFoundException e) {
                    WriteError(e, "PackageNotFound", ErrorCategory.ObjectNotFound, pn);
                } catch (InvalidPackageNameException e) {
                    WriteError(e, "InvalidPackageName", ErrorCategory.InvalidArgument, pn);
                }
            }
        }
    }
}
