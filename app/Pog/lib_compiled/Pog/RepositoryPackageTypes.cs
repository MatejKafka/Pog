using System;
using IOPath = System.IO.Path;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

[PublicAPI]
public class Repository {
    public readonly string Path;

    public Repository(string manifestRepositoryDirPath) {
        Path = manifestRepositoryDirPath;
    }

    public IEnumerable<string> EnumeratePackageNames(string searchPattern = "*") {
        return PathUtils.EnumerateNonHiddenDirectoryNames(Path, searchPattern);
    }

    public IEnumerable<RepositoryVersionedPackage> Enumerate(string searchPattern = "*") {
        var repo = this;
        return EnumeratePackageNames(searchPattern).Select(p => new RepositoryVersionedPackage(p, repo));
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
        Debug.Assert(PathUtils.IsValidFileName(packageName));
        PackageName = packageName;
        Repository = repository;
        Path = IOPath.Combine(repository.Path, packageName);
    }

    /// Enumerate package versions, ordered semantically by the version.
    public IEnumerable<RepositoryPackage> Enumerate(string searchPattern = "*") {
        return EnumerateVersions(searchPattern)
                .Select(v => new RepositoryPackage(this, v));
    }

    /// Enumerate parsed versions of the package, in a DESCENDING order.
    public IEnumerable<PackageVersion> EnumerateVersions(string searchPattern = "*") {
        return EnumerateVersionStrings(searchPattern)
                .Select(v => new PackageVersion(v))
                .OrderByDescending(v => v);
    }

    /// Enumerate UNORDERED versions of the package.
    public IEnumerable<string> EnumerateVersionStrings(string searchPattern = "*") {
        try {
            return PathUtils.EnumerateNonHiddenDirectoryNames(Path, searchPattern);
        } catch (DirectoryNotFoundException) {
            return Enumerable.Empty<string>();
        }
    }

    public RepositoryPackage GetVersionPackage(string version) {
        return GetVersionPackage(new PackageVersion(version));
    }

    public RepositoryPackage GetVersionPackage(PackageVersion version) {
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