using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

/// <summary>Disables a package, preventing further use and reverting all externally visible changes.</summary>
/// <para>
/// Disables a package, removing exported commands and shortcuts and cleaning up any external modifications.
/// After this command completes, there should not be any leftovers from the package outside its package directory.
/// </para>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Disable, "Pog", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class DisablePogCommand() : ImportedPackageCommand(true) {
    protected override void ProcessPackage(ImportedPackage package) {
        InvokePogCommand(new DisablePog(this) {
            Package = package,
        });
    }
}
