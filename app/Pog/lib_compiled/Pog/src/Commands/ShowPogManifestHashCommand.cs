using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

/// <summary>Downloads all resources needed to install the given package and shows the SHA-256 hashes.</summary>
/// <para>
/// Download all resources specified in the package manifest, store them in the download cache and show the SHA-256 hash.
/// This cmdlet is useful for retrieving the hashes when writing a package manifest.
/// </para>
[PublicAPI]
[Cmdlet(VerbsCommon.Show, "PogManifestHash", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class ShowPogManifestHashCommand : RepositoryPackageCommand {
    private const string ImportedPS = "ImportedPackage";

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ImportedPS, ValueFromPipeline = true)]
    public ImportedPackage[] ImportedPackage = null!; // accept even imported packages

    /// Download files with low priority, which results in better network responsiveness
    /// for other programs, but possibly slower download speed.
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
            Context = new DownloadContainerContext(package, LowPriority),
            Modules = [$@"{InternalState.PathConfig.ContainerDir}\Env_GetInstallHash.psm1"],
            // $this is used inside the manifest to refer to fields of the manifest itself to emulate class-like behavior
            Variables = [new("this", package.Manifest.Raw, "Loaded manifest of the processed package")],
            Run = ps => ps.AddCommand("__main").AddArgument(package.Manifest),
        });

        // GetInstallHash container should not output anything, show a warning
        foreach (var o in it) {
            WriteWarning($"GET_INSTALL_HASH: {o}");
        }
    }
}
