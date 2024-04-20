using System.Collections.Generic;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;

namespace Pog.Commands;

/// <summary>
/// <para type="synopsis">Lists packages available in the package repository.</para>
/// <para type="description">
/// The `Get-PogRepositoryPackage` cmdlet lists packages from the package repository.
/// Each package is represented by a single `Pog.RepositoryPackage` instance. By default, only the latest version
/// of each package is returned. If you want to list all available versions, use the `-AllVersions` switch parameter.
/// </para>
/// </summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "PogRepositoryPackage", DefaultParameterSetName = VersionPS)]
[OutputType(typeof(RepositoryPackage))]
public sealed class GetPogRepositoryPackageCommand : PogCmdlet {
    private const string VersionPS = "Version";
    private const string AllVersionsPS = "AllVersions";

    /// <summary><para type="description">
    /// Names of packages to return. If not passed, all repository packages are returned.
    /// </para></summary>
    [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [ValidateNotNullOrEmpty]
    [ArgumentCompleter(typeof(PSAttributes.RepositoryPackageNameCompleter))]
    public string[]? PackageName;

    // TODO: figure out how to remove this parameter when -PackageName is an array
    /// <summary><para type="description">
    /// Return only a single package with the given version. An exception is thrown if the version is not found.
    /// </para></summary>
    [Parameter(Position = 1, ParameterSetName = VersionPS)]
    [ArgumentCompleter(typeof(PSAttributes.RepositoryPackageVersionCompleter))]
    public PackageVersion? Version;

    /// <summary><para type="description">
    /// Return all available versions of each repository package. By default, only the latest one is returned.
    /// </para></summary>
    [Parameter(ParameterSetName = AllVersionsPS)]
    public SwitchParameter AllVersions;

    /// <summary><para type="description">
    /// Load manifests for all returned packages. This is typically significantly faster than calling
    /// <see cref="Package.ReloadManifest()"/> separately on each package, since the loading is parallelized.
    /// </para></summary>
    [Parameter] public SwitchParameter LoadManifest;

    /// List of asynchronously loading manifests.
    private readonly List<(RepositoryPackage, Task)> _pendingManifestLoads = [];
    private readonly CancellationTokenSource _stopping = new();

    public override void Dispose() {
        base.Dispose();
        _stopping.Dispose();
    }

    protected override void StopProcessing() {
        base.StopProcessing();
        _stopping.Cancel();
    }

    protected override void BeginProcessing() {
        base.BeginProcessing();

        if (Version != null) {
            if (MyInvocation.ExpectingInput) {
                ThrowTerminatingArgumentError(Version, "VersionWithPipelineInput",
                        "-Version must not be passed together with pipeline input.");
            } else if (PackageName == null) {
                ThrowTerminatingArgumentError(Version, "VersionWithoutPackage",
                        "-Version must not be passed without also passing -PackageName.");
            } else if (PackageName.Length > 1) {
                ThrowTerminatingArgumentError(Version, "VersionWithMultiplePackages",
                        "-Version must not be passed when -PackageName contains multiple package names.");
            }

            try {
                var package = InternalState.Repository
                        .GetPackage(PackageName![0], true, true)
                        .GetVersionPackage(Version, true);
                if (LoadManifest) {
                    package.ReloadManifest();
                }
                WritePackage(package);
            } catch (RepositoryPackageNotFoundException e) {
                WriteError(e, "PackageNotFound", ErrorCategory.ObjectNotFound, PackageName![0]);
            } catch (RepositoryPackageVersionNotFoundException e) {
                WriteError(e, "PackageVersionNotFound", ErrorCategory.ObjectNotFound, Version);
            }
        }
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();

        if (Version != null) {
            return; // already processed above
        }

        if (PackageName == null) {
            foreach (var vp in InternalState.Repository.Enumerate()) {
                ProcessPackage(vp);
            }
        } else {
            foreach (var pn in PackageName) {
                if (WildcardPattern.ContainsWildcardCharacters(pn)) {
                    foreach (var vp in InternalState.Repository.Enumerate(pn)) {
                        ProcessPackage(vp);
                    }
                } else {
                    try {
                        ProcessPackage(InternalState.Repository.GetPackage(pn, true, true));
                    } catch (RepositoryPackageNotFoundException e) {
                        WriteError(e, "PackageNotFound", ErrorCategory.ObjectNotFound, pn);
                    } catch (InvalidPackageNameException e) {
                        WriteError(e, "InvalidPackageName", ErrorCategory.InvalidArgument, pn);
                    }
                }
            }
        }
    }

    private void ProcessPackage(RepositoryVersionedPackage package) {
        if (AllVersions) {
            foreach (var o in package.Enumerate()) {
                WritePackage(o);
            }
        } else {
            try {
                WritePackage(package.GetLatestPackage());
            } catch (RepositoryPackageVersionNotFoundException e) {
                WriteError(e, "NoPackageVersionExists", ErrorCategory.ObjectNotFound, package.PackageName);
            }
        }
    }

    protected override void EndProcessing() {
        base.EndProcessing();

        if (_pendingManifestLoads.Count == 0) {
            return;
        }

        foreach (var (rp, manifestTask) in _pendingManifestLoads) {
            try {
                manifestTask.GetAwaiter().GetResult();
                WriteObject(rp);
            } catch (PackageManifestNotFoundException e) {
                WriteError(e, "ManifestNotFound", ErrorCategory.InvalidData, rp);
            } catch (PackageManifestParseException e) {
                WriteError(e, "InvalidPackageManifest", ErrorCategory.InvalidData, rp);
            } catch (InvalidPackageManifestStructureException e) {
                WriteError(e, "InvalidPackageManifest", ErrorCategory.InvalidData, rp);
            }
        }
    }

    private void WritePackage(RepositoryPackage p) {
        if (!LoadManifest) {
            WriteObject(p);
            return;
        }

        // for remote packages, it takes a long time to load the manifest, since we're downloading a zip archive for each
        //  package; even for local repositories, parallelizing the load of manifests should improve performance, since the
        //  parsing takes some time; parallelize the loading and return the packages from `EndProcessing`
        _pendingManifestLoads.Add((p, p.ReloadManifestAsync(_stopping.Token)));
    }
}
