using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace Pog;

public class RepositoryNotFoundException(string message) : Exception(message);

public class RepositoryPackageNotFoundException(string message) : PackageNotFoundException(message);

public class RepositoryPackageVersionNotFoundException(string message) : PackageNotFoundException(message);

// it would be nice if we could express in the type system that we have 2 separate hierarchies
//  (e.g. RemoteRepository always returns RemoteRepositoryVersionedPackage), but since .netstandard2.0 does not support
//  covariant return types, we would have to express it with generics, which would prevent us from having a list of repositories
[PublicAPI]
public interface IRepository {
    public bool Exists {get;}
    public IEnumerable<string> EnumeratePackageNames(string searchPattern = "*");
    public IEnumerable<RepositoryVersionedPackage> Enumerate(string searchPattern = "*");
    public RepositoryVersionedPackage GetPackage(string packageName, bool resolveName, bool mustExist);
}

/// <summary>
/// Class representing a package with multiple available versions. Each version is represented by an `IRepositoryPackage`.
/// </summary>
/// The backing directory may not exist, in which case the enumeration methods behave as if there were no existing versions.
[PublicAPI]
public abstract class RepositoryVersionedPackage {
    public readonly string PackageName;
    public abstract IRepository Repository {get;}
    public abstract bool Exists {get;}

    protected RepositoryVersionedPackage(string packageName) {
        Verify.Assert.PackageName(packageName);
        PackageName = packageName;
    }

    protected abstract string ExpectedPathStr {get;}
    protected abstract RepositoryPackage GetPackageUnchecked(PackageVersion version);

    /// Enumerate UNORDERED versions of the package.
    public abstract IEnumerable<string> EnumerateVersionStrings(string searchPattern = "*");

    /// Enumerate parsed versions of the package, in a DESCENDING order.
    public virtual IEnumerable<PackageVersion> EnumerateVersions(string searchPattern = "*") {
        return EnumerateVersionStrings(searchPattern)
                .Select(v => new PackageVersion(v))
                .OrderByDescending(v => v);
    }

    /// Enumerate packages for versions matching the pattern, in a DESCENDING order according to the version.
    public IEnumerable<RepositoryPackage> Enumerate(string searchPattern = "*") {
        return EnumerateVersions(searchPattern).Select(GetPackageUnchecked);
    }

    public RepositoryPackage? TryGetLatestPackage() {
        var latestVersion = EnumerateVersionStrings().Select(v => new PackageVersion(v)).Max();
        return latestVersion == null ? null : GetPackageUnchecked(latestVersion);
    }

    public RepositoryPackage GetLatestPackage() {
        return TryGetLatestPackage() ?? throw new RepositoryPackageVersionNotFoundException(
                $"Package '{PackageName}' in the repository does not have any versions, {ExpectedPathStr}");
    }

    public RepositoryPackage GetVersionPackage(string version, bool mustExist) {
        return GetVersionPackage(new PackageVersion(version), mustExist);
    }

    public virtual RepositoryPackage GetVersionPackage(PackageVersion version, bool mustExist) {
        var package = GetPackageUnchecked(version);
        if (mustExist && !package.Exists) {
            throw new RepositoryPackageVersionNotFoundException(
                    $"Package '{PackageName}' in the repository does not have version '{version}', {ExpectedPathStr}");
        }
        return package;
    }
}

[PublicAPI]
public abstract class RepositoryPackage(RepositoryVersionedPackage parent, PackageVersion version)
        : Package(parent.PackageName, null) {
    public readonly RepositoryVersionedPackage Container = parent;
    public readonly PackageVersion Version = version;

    public override string GetDescriptionString() => $"package '{PackageName}', version '{Version}'";

    public abstract bool MatchesImportedManifest(ImportedPackage p);
    public abstract void ImportTo(ImportedPackage target);
}
