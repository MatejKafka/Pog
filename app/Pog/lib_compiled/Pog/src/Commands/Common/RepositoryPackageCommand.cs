using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog.Commands.Common;

[PublicAPI]
public abstract class RepositoryPackageCommand : PackageCommandBase {
    protected const string PackagePS = "Package";
    protected const string PackageNamePS = "PackageName";
    protected const string DefaultPS = PackageNamePS;

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackagePS, ValueFromPipeline = true)]
    public RepositoryPackage[] Package = null!;

    /// <summary><para type="description">Name of the repository package.</para></summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageNamePS, ValueFromPipeline = true)]
    [ArgumentCompleter(typeof(PSAttributes.RepositoryPackageNameCompleter))]
    public string[] PackageName = null!;

    /// <summary><para type="description">
    /// Version of the repository package to retrieve. By default, the latest version is used.
    /// </para></summary>
    [Parameter(Position = 1, ParameterSetName = PackageNamePS)]
    [ArgumentCompleter(typeof(PSAttributes.RepositoryPackageVersionCompleter))]
    public PackageVersion? Version;

#if DEBUG
    protected RepositoryPackageCommand() : base(DefaultPS) {}
#endif

    protected override void BeginProcessing() {
        base.BeginProcessing();

        if (Version != null) {
            if (MyInvocation.ExpectingInput) {
                ThrowTerminatingArgumentError(Version, "VersionWithPipelineInput",
                        "-Version must not be passed together with pipeline input.");
            } else if (PackageName.Length > 1) {
                ThrowTerminatingArgumentError(Version, "VersionWithMultiplePackages",
                        "-Version must not be passed when -PackageName contains multiple package names.");
            }

            if (GetRepositoryPackage(PackageName[0], Version) is {} package) {
                ProcessPackage(package);
            }
        }
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();

        if (Version != null) {
            return; // already processed above
        }

        var packages = ParameterSetName == PackagePS ? Package : GetRepositoryPackage(PackageName);
        // TODO: do this in parallel (even for packages passed as array)
        foreach (var package in packages) {
            ProcessPackage(package);
        }
    }

    protected abstract void ProcessPackage(RepositoryPackage package);
}
