using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog.Commands.Common;

[PublicAPI]
[OutputType(typeof(ImportedPackage))]
public abstract class ImportedPackageCommand : ImportedPackageNoPassThruCommand {
    /// <summary><para type="description">
    /// Return a [Pog.ImportedPackage] object with information about the package.
    /// </para></summary>
    [Parameter] public SwitchParameter PassThru;

    protected sealed override void ProcessPackageNoPassThru(ImportedPackage package) {
        ProcessPackage(package);
        if (PassThru) {
            WriteObject(package);
        }
    }

    protected abstract void ProcessPackage(ImportedPackage package);
}
