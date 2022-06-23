using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IOPath = System.IO.Path;
using System.Management.Automation;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Pog;

internal static class Utils {
    public static IEnumerable<string> EnumerateNonHiddenDirectoryNames(string path) {
        return new DirectoryInfo(path).EnumerateDirectories()
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(d => d.Name);
    }

    public static IEnumerable<string> EnumerateNonHiddenDirectoryNames(string path, string searchPattern) {
        return new DirectoryInfo(path).EnumerateDirectories(searchPattern)
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(d => d.Name);
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

    public IEnumerable<RepositoryContainer> Enumerate() {
        var repo = this;
        return EnumeratePackageNames().Select(p => new RepositoryContainer(p, repo));
    }

    public RepositoryContainer GetContainer(string packageName, bool resolveName = false) {
        if (resolveName) {
            try {
                packageName = Utils.EnumerateNonHiddenDirectoryNames(Path, packageName).First();
            } catch (InvalidOperationException) {} // the container does not exist yet, use the passed package name
        }
        return new RepositoryContainer(packageName, this);
    }
}

/// <summary>
/// Class representing the repository directory containing package directories for different versions.
/// </summary>
/// The backing directory may not exist, in which case the enumeration methods behave as if it was empty. 
[PublicAPI]
public class RepositoryContainer {
    public readonly string PackageName;
    [Hidden] public readonly Repository Repository;
    public readonly string Path;
    [Hidden] public bool Exists => Directory.Exists(this.Path);

    public RepositoryContainer(string packageName, Repository repository) {
        PackageName = packageName;
        Repository = repository;
        Path = IOPath.Combine(repository.Path, packageName);
    }

    public IEnumerable<RepositoryPackage> EnumerateSorted() {
        return EnumerateVersions()
                .Select(v => new RepositoryPackage(this, new PackageVersion(v)))
                .OrderBy(p => p.Version);
    }

    public IEnumerable<string> EnumerateVersions() {
        try {
            return Utils.EnumerateNonHiddenDirectoryNames(Path);
        } catch (DirectoryNotFoundException) {
            return Enumerable.Empty<string>();
        }
    }

    public RepositoryPackage GetPackage(string version) {
        return GetPackage(new PackageVersion(version));
    }

    public RepositoryPackage GetPackage(PackageVersion version) {
        return new RepositoryPackage(this, version);
    }

    public PackageVersion GetLatestVersion() {
        return EnumerateVersions().Select(v => new PackageVersion(v)).Max();
    }

    public RepositoryPackage GetLatestPackage() {
        return new RepositoryPackage(this, GetLatestVersion());
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
}

[PublicAPI]
public class RepositoryPackage : Package {
    public readonly PackageVersion Version;
    [Hidden] public readonly RepositoryContainer Container;

    public RepositoryPackage(string packageName, PackageVersion version, Repository repository)
            : this(new RepositoryContainer(packageName, repository), version) {}

    public RepositoryPackage(RepositoryContainer parent, PackageVersion version)
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
        var packageName = IOPath.GetFileName(packagePath);
        try {
            packageName = Utils.EnumerateNonHiddenDirectoryNames(parentPath, packageName).First();
        } catch (InvalidOperationException) {} // the package does not exist yet, use the passed package name
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