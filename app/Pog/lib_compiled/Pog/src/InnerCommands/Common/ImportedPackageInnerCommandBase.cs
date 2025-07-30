using System.Management.Automation;

namespace Pog.InnerCommands.Common;

public abstract class ImportedPackageInnerCommandBase(PogCmdlet cmdlet) : VoidCommand(cmdlet) {
    [Parameter] public required ImportedPackage Package;
}
