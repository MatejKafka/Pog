using System.Management.Automation;
using JetBrains.Annotations;
using Pog.PSAttributes;
using Pog.Utils;

namespace Pog.Commands.Common;

[PublicAPI]
public abstract class ImportedPackageNoPassThruCommand : PackageCommandBase {
    protected const string PackagePS = "Package";
    protected const string PackageNamePS = "PackageName";
    protected const string DefaultPS = PackageNamePS;

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackagePS, ValueFromPipeline = true)]
    public ImportedPackage[] Package = null!;

    /// <summary><para type="description">
    /// Name of the package. This is the target name, not necessarily the manifest app name.
    /// </para></summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageNamePS, ValueFromPipeline = true)]
    [ArgumentCompleter(typeof(ImportedPackageNameCompleter))]
    public string[] PackageName = null!;

#if DEBUG
    protected ImportedPackageNoPassThruCommand() : base(DefaultPS) {}
#endif

    protected override void ProcessRecord() {
        base.ProcessRecord();

        var packages = ParameterSetName == PackagePS ? Package : PackageName.SelectOptional(GetImportedPackage);
        // TODO: do this in parallel (even for packages passed as array)
        foreach (var package in packages) {
            ProcessPackageNoPassThru(package);
        }
    }

    protected abstract void ProcessPackageNoPassThru(ImportedPackage package);
}
