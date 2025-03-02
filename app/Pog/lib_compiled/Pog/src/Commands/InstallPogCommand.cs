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
    /// Download files with low priority, which results in better network responsiveness
    /// for other programs, but possibly slower download speed.
    [Parameter] public SwitchParameter LowPriority;

    protected override void ProcessPackage(ImportedPackage package) {
        if (package.Manifest.Install == null) {
            WriteInformation($"Package '{package.PackageName}' does not have an Install block.");
            return;
        }

        WriteInformation($"Installing {package.GetDescriptionString()}...");

        InvokePogCommand(new InstallFromUrl(this) {
            Package = package,
            LowPriorityDownload = LowPriority,
        });
    }
}
