using System.Collections;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

/// <summary>
/// <para type="synopsis">Download resources for the given package and show the SHA-256 hashes.</para>
/// <para type="description">
/// Download all resources specified in the package manifest, store them in the download cache and show the SHA-256 hash.
/// This cmdlet is useful for retrieving the archive hash when writing a package manifest.
/// </para>
/// </summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Show, "PogManifestHash", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class ShowPogManifestHashCommand : RepositoryPackageCommand {
    private const string ImportedPS = "ImportedPackage";

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ImportedPS, ValueFromPipeline = true)]
    public ImportedPackage[] ImportedPackage = null!; // accept even imported packages

    /// <summary><para type="description">
    /// Download files with low priority, which results in better network responsiveness
    /// for other programs, but possibly slower download speed.
    /// </para></summary>
    [Parameter]
    public SwitchParameter LowPriority;

    protected override void ProcessRecord() {
        if (ParameterSetName != ImportedPS) {
            base.ProcessRecord();
            return;
        }

        // TODO: do this in parallel (even for packages passed as array)
        foreach (var package in ImportedPackage) {
            ProcessPackage(package);
        }
    }

    protected override void ProcessPackage(RepositoryPackage package) => ProcessPackage(package);

    private bool _first = true;

    private void ProcessPackage(Package package) {
        package.EnsureManifestIsLoaded();
        if (package.Manifest.Install == null) {
            WriteInformation($"Package '{package.PackageName}' does not have an Install block.");
            return;
        }

        if (_first) _first = false;
        else WriteHost("");

        var it = InvokePogCommand(new InvokeContainer(this) {
            ContainerType = Container.ContainerType.GetInstallHash,
            Package = package,
            InternalArguments = new Hashtable {
                {"AllowOverwrite", true}, // not used
                {"DownloadLowPriority", (bool) LowPriority},
            },
        });

        // GetInstallHash container should not output anything, show a warning
        foreach (var o in it) {
            WriteWarning($"GET_INSTALL_HASH: {o}");
        }
    }
}
