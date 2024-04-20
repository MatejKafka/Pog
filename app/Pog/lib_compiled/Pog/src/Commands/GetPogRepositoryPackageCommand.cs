using System.Management.Automation;
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

    private readonly IRepository _packages = InternalState.Repository;

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
                WriteObject(_packages.GetPackage(PackageName![0], true, true).GetVersionPackage(Version, true));
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
            foreach (var vp in _packages.Enumerate()) {
                ProcessPackage(vp);
            }
        } else {
            foreach (var pn in PackageName) {
                if (WildcardPattern.ContainsWildcardCharacters(pn)) {
                    foreach (var vp in _packages.Enumerate(pn)) {
                        ProcessPackage(vp);
                    }
                } else {
                    try {
                        ProcessPackage(_packages.GetPackage(pn, true, true));
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
            WriteObjectEnumerable(package.Enumerate());
        } else {
            try {
                WriteObject(package.GetLatestPackage());
            } catch (RepositoryPackageVersionNotFoundException e) {
                WriteError(e, "NoPackageVersionExists", ErrorCategory.ObjectNotFound, package.PackageName);
            }
        }
    }
}
