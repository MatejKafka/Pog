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
        // enumerate exported items and delete them
        // do this before disabling the package, so that prevent anyone from calling a disabled package
        // FIXME: the "Disabling package ..." print is only done in DisablePog, this runs before it
        InvokePogCommand(new UnexportPog(this) {
            Package = package,
        });

        // if app has an optional Disable block, run it
        InvokePogCommand(new DisablePog(this) {
            Package = package,
        });
    }
}
