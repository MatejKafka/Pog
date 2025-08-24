using System.Management.Automation;
using Pog.Commands.Common;

namespace Pog.InnerCommands.Common;

internal abstract class ImportedPackageInnerCommandBase(PogCmdlet cmdlet) : ScalarCommand<bool>(cmdlet) {
    [Parameter] public required ImportedPackage Package;
}
