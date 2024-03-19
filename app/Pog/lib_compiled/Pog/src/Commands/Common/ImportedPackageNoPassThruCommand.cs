using System.Collections.Generic;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.PSAttributes;

namespace Pog.Commands.Common;

[PublicAPI]
public abstract class ImportedPackageNoPassThruCommand : PackageCommandBase {
    protected const string PackagePS = "Package";
    protected const string PackageNamePS = "PackageName";
    protected const string DefaultPS = PackageNamePS;

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackagePS, ValueFromPipeline = true)]
    public ImportedPackage[]? Package = null;

    /// <summary><para type="description">
    /// Name of the package. This is the target name, not necessarily the manifest app name.
    /// </para></summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageNamePS, ValueFromPipeline = true)]
    [ArgumentCompleter(typeof(ImportedPackageNameCompleter))]
    public string[]? PackageName = null;

    private bool _loadManifest;

#if DEBUG
    private ImportedPackageNoPassThruCommand() : base(DefaultPS) {}
#else
    private ImportedPackageNoPassThruCommand() {}
#endif

    protected ImportedPackageNoPassThruCommand(bool loadManifest) : this() {
        _loadManifest = loadManifest;
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();

        // TODO: do this in parallel (even for packages passed as array)
        foreach (var package in EnumerateParameterPackages()) {
            if (ShouldProcess(package.PackageName)) {
                ProcessPackageNoPassThru(package);
            }
        }
    }

    protected abstract void ProcessPackageNoPassThru(ImportedPackage package);

    protected IEnumerable<ImportedPackage> EnumerateParameterPackages() {
        // all parameters are null-coalesced, because this is also called from `GetDynamicParameters()`,
        //  where mandatory parameters are not enforced
        return ParameterSetName == PackagePS
                ? _loadManifest ? EnsureManifestIsLoaded(Package ?? []) : Package ?? []
                : GetImportedPackage(PackageName ?? [], _loadManifest);
    }
}
