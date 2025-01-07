using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog.Commands.Common;

[PublicAPI]
[OutputType(typeof(ImportedPackage))]
public abstract class ImportedPackageCommand(bool loadManifest) : ImportedPackageNoPassThruCommand(loadManifest) {
    /// Return a [Pog.ImportedPackage] object representing the package.
    [Parameter] public SwitchParameter PassThru;

    protected sealed override void ProcessPackageNoPassThru(ImportedPackage package) {
        ProcessPackage(package);
        if (PassThru) {
            WriteObject(package);
        }
    }

    protected abstract void ProcessPackage(ImportedPackage package);
}
