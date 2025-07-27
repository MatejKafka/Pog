using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

/// <summary>Downloads and extracts package files.</summary>
/// <para>
/// Downloads and extracts package files, populating the ./app directory of the package. Downloaded files
/// are cached, so repeated installs only require internet connection for the initial download.
/// </para>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Install, "Pog", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class InstallPogCommand() : ImportedPackageCommand(true) {
    protected override void ProcessPackage(ImportedPackage package) {
        InvokePogCommand(new InstallPog(this) {
            Package = package,
        });
    }
}
