using System.Collections.Generic;
using System.Management.Automation;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.PSAttributes;

namespace Pog.Commands;

/// <summary>Lists packages available in the package repository.</summary>
/// <para>
/// The `Find-Pog` cmdlet lists packages from the package repository.
/// Each package is represented by a single `Pog.RepositoryPackage` instance. By default, only the latest version
/// of each package is returned. If you want to list all available versions, use the `-AllVersions` switch parameter.
/// </para>
[PublicAPI]
[Cmdlet(VerbsCommon.Find, "Pog", DefaultParameterSetName = VersionPS)]
[OutputType(typeof(RepositoryPackage))]
public sealed class FindPogCommand : PogCmdlet {
    private const string VersionPS = "Version";
    private const string AllVersionsPS = "AllVersions";

    /// Names of packages to return. If not passed, all repository packages are returned.
    [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    [ValidateNotNullOrEmpty]
    [ArgumentCompleter(typeof(RepositoryPackageNameCompleter))]
    public string[]? PackageName;

    /// Return the specified versions of a single package.
    /// This parameter is only allowed when a single package name is passed in <see cref="PackageName"/>.
    [Parameter(Position = 1, ParameterSetName = VersionPS)]
    [SupportsWildcards]
    [ArgumentCompleter(typeof(RepositoryPackageVersionCompleter))]
    public string[]? Version;

    /// Return all available versions of each repository package. By default, only the latest one is returned.
    [Parameter(ParameterSetName = AllVersionsPS)]
    public SwitchParameter AllVersions;

    /// Load manifests for all returned packages. This is typically significantly faster than calling
    /// <see cref="Package.ReloadManifest()"/> separately on each package, since the loading is parallelized.
    [Parameter] public SwitchParameter LoadManifest;

    /// List of asynchronously loading manifests.
    private readonly List<(RepositoryPackage, Task)> _pendingManifestLoads = [];

    protected override void BeginProcessing() {
        base.BeginProcessing();

        if (Version != null) {
            if (MyInvocation.ExpectingInput) {
                ThrowArgumentError(Version, "VersionWithPipelineInput",
                        "-Version must not be passed together with pipeline input.");
            } else if (PackageName == null) {
                ThrowArgumentError(Version, "VersionWithoutPackage",
                        "-Version must not be passed without also passing -PackageName.");
            } else if (PackageName.Length > 1) {
                ThrowArgumentError(Version, "VersionWithMultiplePackages",
                        "-Version must not be passed when -PackageName contains multiple package names.");
            }

            ListSpecificVersions(PackageName![0], Version);
        }
    }

    private void ListSpecificVersions(string packageName, string[] versions) {
        RepositoryVersionedPackage vp;
        try {
            vp = InternalState.Repository.GetPackage(packageName, true, true);
        } catch (RepositoryPackageNotFoundException e) {
            WriteError(e, "PackageNotFound", ErrorCategory.ObjectNotFound, PackageName![0]);
            return;
        }

        foreach (var v in versions) {
            if (WildcardPattern.ContainsWildcardCharacters(v)) {
                foreach (var p in vp.Enumerate(v)) {
                    WritePackage(p);
                }
            } else {
                if (GetPackageVersion(vp, new(v)) is {} p) {
                    WritePackage(p);
                }
            }
        }
    }

    private RepositoryPackage? GetPackageVersion(RepositoryVersionedPackage vp, PackageVersion version) {
        try {
            return vp.GetVersionPackage(version, true);
        } catch (RepositoryPackageVersionNotFoundException e) {
            WriteError(e, "PackageVersionNotFound", ErrorCategory.ObjectNotFound, Version);
            return null;
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
        _pendingManifestLoads.Add((p, p.ReloadManifestAsync(CancellationToken)));
    }
}
