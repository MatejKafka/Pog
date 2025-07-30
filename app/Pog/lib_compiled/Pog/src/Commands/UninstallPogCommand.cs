using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

/// <summary>Uninstalls a package.</summary>
/// <para>
/// Uninstalls a package by first disabling it (see `Disable-Pog`) and then deleting the package directory.
/// If -KeepData is passed, only the app, cache and logs directories are deleted and persistent data are left intact.
/// </para>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Uninstall, "Pog", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class UninstallPogCommand() : ImportedPackageNoPassThruCommand(false) {
    /// Keep the package directory, only disable the package and delete the app directory.
    [Parameter] public SwitchParameter KeepData;

    // does not make sense to support -PassThru for this cmdlet
    protected override void ProcessPackageNoPassThru(ImportedPackage package) {
        InvokePogCommand(new UninstallPog(this) {
            Package = package,
            KeepData = KeepData,
        });
    }
}
