using System.Management.Automation;
using JetBrains.Annotations;
using Pog.PSAttributes;

namespace Pog.Commands.Common;

[PublicAPI]
public abstract class RepositoryPackageCommand : PackageCommandBase {
    protected const string PackagePS = "Package";
    protected const string PackageNamePS = "PackageName";
    protected const string DefaultPS = PackageNamePS;

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackagePS, ValueFromPipeline = true)]
    public RepositoryPackage[] Package = null!;

    /// Name of the repository package.
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageNamePS, ValueFromPipeline = true)]
    [ArgumentCompleter(typeof(RepositoryPackageNameCompleter))]
    public string[] PackageName = null!;

    /// Version of the repository package to retrieve. By default, the latest version is used.
    [Parameter(Position = 1, ParameterSetName = PackageNamePS)]
    [ArgumentCompleter(typeof(RepositoryPackageVersionCompleter))]
    public PackageVersion? Version;

#if DEBUG
    protected RepositoryPackageCommand() : base(DefaultPS) {}
#endif

    protected override void BeginProcessing() {
        base.BeginProcessing();

        if (Version != null) {
            if (MyInvocation.ExpectingInput) {
                ThrowArgumentError(Version, "VersionWithPipelineInput",
                        "-Version must not be passed together with pipeline input.");
            } else if (PackageName.Length > 1) {
                ThrowArgumentError(Version, "VersionWithMultiplePackages",
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
