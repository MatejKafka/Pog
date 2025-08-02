using System.Management.Automation;
using Pog.Commands.Common;

namespace Pog.InnerCommands.Common;

internal abstract class ImportedPackageInnerCommandBase(PogCmdlet cmdlet) : VoidCommand(cmdlet) {
    [Parameter] public required ImportedPackage Package;
}
