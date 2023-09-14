using System.Collections;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

/// <summary>
/// <para type="synopsis">Downloads and extracts package files.</para>
/// <para type="description">
/// Downloads and extracts package files, populating the ./app directory of the package. Downloaded files
/// are cached, so repeated installs only require internet connection for the initial download.
/// </para>
/// </summary>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Install, "Pog", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public class InstallPogCommand : ImportedPackageCommand {
    /// <summary><para type="description">
    /// Download files with low priority, which results in better network responsiveness
    /// for other programs, but possibly slower download speed.
    /// </para></summary>
    [Parameter] public SwitchParameter LowPriority;

    protected override void ProcessPackage(ImportedPackage package) {
        package.EnsureManifestIsLoaded();
        if (package.Manifest.Install == null) {
            WriteInformation($"Package '{package.PackageName}' does not have an Install block.");
            return;
        }

        WriteInformation($"Installing {package.GetDescriptionString()}...");

        var it = InvokePogCommand(new InvokeContainer(this) {
            ContainerType = Container.ContainerType.Install,
            Package = package,
            InternalArguments = new Hashtable {
                {"AllowOverwrite", true},
                {"DownloadLowPriority", (bool) LowPriority},
            },
        });

        // Install container should not output anything, show a warning
        foreach (var o in it) {
            WriteWarning($"INSTALL: {o}");
        }
    }
}
