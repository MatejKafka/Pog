using IOPath = System.IO.Path;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

[PublicAPI]
public readonly struct Repository {
    [Hidden] public readonly string Path;

    public Repository(string manifestRepositoryDirPath) {
        Path = manifestRepositoryDirPath;
    }

    public IEnumerable<string> EnumeratePackageNames() {
        return PathUtils.EnumerateNonHiddenDirectoryNames(Path);
    }

    public IEnumerable<RepositoryVersionedPackage> Enumerate() {
        var repo = this;
        return EnumeratePackageNames().Select(p => new RepositoryVersionedPackage(p, repo));
    }

    public RepositoryVersionedPackage GetPackage(string packageName, bool resolveName) {
        if (resolveName) {
            packageName = PathUtils.GetResolvedChildName(Path, packageName);
        }
        return new RepositoryVersionedPackage(packageName, this);
    }
}

/// <summary>
/// Class representing a repository directory containing different versions of a RepositoryPackage.
/// </summary>
/// The backing directory may not exist, in which case the enumeration methods behave as if it was empty.
[PublicAPI]
public class RepositoryVersionedPackage {
    public readonly string PackageName;
    [Hidden] public readonly Repository Repository;
    public readonly string Path;
    [Hidden] public bool Exists => Directory.Exists(this.Path);

    internal RepositoryVersionedPackage(string packageName, Repository repository) {
        PackageName = packageName;
        Repository = repository;
        Path = IOPath.Combine(repository.Path, packageName);
    }

    public IEnumerable<RepositoryPackage> EnumerateSorted() {
        return EnumerateVersionStrings()
                .Select(v => new RepositoryPackage(this, new PackageVersion(v)))
                .OrderBy(p => p.Version);
    }

    public IEnumerable<string> EnumerateVersionStrings() {
        try {
            return PathUtils.EnumerateNonHiddenDirectoryNames(Path);
        } catch (DirectoryNotFoundException) {
            return Enumerable.Empty<string>();
        }
    }

    public RepositoryPackage GetVersion(string version) {
        return GetVersion(new PackageVersion(version));
    }

    public RepositoryPackage GetVersion(PackageVersion version) {
        return new RepositoryPackage(this, version);
    }

    public RepositoryPackage GetLatestPackage() {
        var latestVersion = EnumerateVersionStrings().Select(v => new PackageVersion(v)).Max();
        return new RepositoryPackage(this, latestVersion);
    }
}

[PublicAPI]
public class RepositoryPackage : Package {
    public readonly PackageVersion Version;
    [Hidden] public readonly RepositoryVersionedPackage Container;

    internal RepositoryPackage(RepositoryVersionedPackage parent, PackageVersion version)
            : base(parent.PackageName, IOPath.Combine(parent.Path, version.ToString())) {
        Version = version;
        Container = parent;
    }
}