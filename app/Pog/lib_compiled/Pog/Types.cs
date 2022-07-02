using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IOPath = System.IO.Path;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

internal static class Utils {
    public static IEnumerable<string> EnumerateNonHiddenDirectoryNames(string path) {
        return new DirectoryInfo(path).EnumerateDirectories()
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(d => d.Name);
    }

    /// Return `childName`, but with casing matching the name as stored in the filesystem, if it already exists.
    public static string GetResolvedChildName(string parent, string childName) {
        try {
            return new DirectoryInfo(parent).EnumerateDirectories(childName).Single().Name;
        } catch (InvalidOperationException) {
            // the child does not exist yet, return the name as-is
            return childName;
        }
    }
}

[PublicAPI]
public readonly struct Repository {
    [Hidden] public readonly string Path;

    public Repository(string manifestRepositoryDirPath) {
        Path = manifestRepositoryDirPath;
    }

    public IEnumerable<string> EnumeratePackageNames() {
        return Utils.EnumerateNonHiddenDirectoryNames(Path);
    }

    public IEnumerable<RepositoryVersionedPackage> Enumerate() {
        var repo = this;
        return EnumeratePackageNames().Select(p => new RepositoryVersionedPackage(p, repo));
    }

    public RepositoryVersionedPackage GetPackage(string packageName, bool resolveName = false) {
        if (resolveName) {
            packageName = Utils.GetResolvedChildName(Path, packageName);
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
            return Utils.EnumerateNonHiddenDirectoryNames(Path);
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
public class Package {
    public string PackageName {get;}
    [Hidden] public string Path {get;}
    [Hidden] public string ManifestPath {get;}

    [Hidden] public bool Exists => Directory.Exists(this.Path);
    [Hidden] public bool ManifestExists => File.Exists(this.ManifestPath);

    internal Package(string packageName, string packagePath) {
        PackageName = packageName;
        Path = packagePath;
        ManifestPath = IOPath.Combine(Path, PathConfig.PackageManifestRelPath);
    }

    public PackageManifest ReadManifest() {
        if (!Exists) {
            throw new DirectoryNotFoundException("INTERNAL ERROR: Tried to read package manifest of a non-existent" +
                                                 $" package at '${Path}'. Seems like Pog developers fucked something up," +
                                                 " plz send bug report.");
        }
        return new PackageManifest(ManifestPath);
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

/// <summary>
/// Internal Pog class for working with imported packages without having to load the manifest.
/// Use ImportedPackage if the instance is returned to the user.
/// </summary>
[PublicAPI]
public class ImportedPackageRaw : Package {
    public ImportedPackageRaw(string packageName, string packagePath) : base(packageName, packagePath) {}

    // TODO: temporary; when a class representing the package roots is created, move this method there
    public static ImportedPackageRaw CreateResolved(string packagePath) {
        var parentPath = Directory.GetParent(packagePath)!.FullName;
        var packageName = Utils.GetResolvedChildName(parentPath, IOPath.GetFileName(packagePath));
        return new ImportedPackageRaw(packageName, IOPath.Combine(parentPath, packageName));
    }
}

[PublicAPI]
public class ImportedPackage : ImportedPackageRaw {
    public readonly PackageVersion? Version;
    [Hidden] public readonly string? ManifestName;

    public ImportedPackage(string packageName, string path, string? manifestName, PackageVersion? manifestVersion)
            : base(packageName, path) {
        ManifestName = manifestName;
        Version = manifestVersion;
    }

    public ImportedPackage(ImportedPackageRaw p, string? manifestName, PackageVersion? manifestVersion)
            : this(p.PackageName, p.Path, manifestName, manifestVersion) {}
}