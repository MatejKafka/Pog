using IOPath = System.IO.Path;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

public class RepositoryPackageNotFoundException : DirectoryNotFoundException {
    public RepositoryPackageNotFoundException(string message) : base(message) {}
}

public class RepositoryPackageVersionNotFoundException : DirectoryNotFoundException {
    public RepositoryPackageVersionNotFoundException(string message) : base(message) {}
}

[PublicAPI]
public class Repository {
    public readonly string Path;

    public Repository(string manifestRepositoryDirPath) {
        Path = manifestRepositoryDirPath;
    }

    public IEnumerable<string> EnumeratePackageNames(string searchPattern = "*") {
        return FileUtils.EnumerateNonHiddenDirectoryNames(Path, searchPattern);
    }

    public IEnumerable<RepositoryVersionedPackage> Enumerate(string searchPattern = "*") {
        var repo = this;
        return EnumeratePackageNames(searchPattern).Select(p => new RepositoryVersionedPackage(p, repo));
    }

    public RepositoryVersionedPackage GetPackage(string packageName, bool resolveName, bool mustExist) {
        Verify.PackageName(packageName);
        if (resolveName) {
            packageName = FileUtils.GetResolvedChildName(Path, packageName);
        }
        var package = new RepositoryVersionedPackage(packageName, this);
        if (mustExist && !package.Exists) {
            throw new RepositoryPackageNotFoundException(
                    $"Package '{package.PackageName}' does not exist in the repository, expected path: {package.Path}");
        }
        return package;
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
    [Hidden] public bool Exists => Directory.Exists(Path);
    [Hidden] public bool IsTemplated => Directory.Exists(IOPath.Combine(Path, TemplatedRepositoryPackage.TemplateDirName));

    internal RepositoryVersionedPackage(string packageName, Repository repository) {
        Verify.Assert.PackageName(packageName);
        PackageName = packageName;
        Repository = repository;
        Path = IOPath.Combine(repository.Path, packageName);
    }

    /// Enumerate package versions, ordered semantically by the version.
    public IEnumerable<RepositoryPackage> Enumerate(string searchPattern = "*") {
        return EnumerateVersions(searchPattern).Select(GetPackage);
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
            if (IsTemplated) {
                return FileUtils.EnumerateNonHiddenFileNames(Path, searchPattern + ".psd1")
                        .Select(IOPath.GetFileNameWithoutExtension);
            } else {
                return FileUtils.EnumerateNonHiddenDirectoryNames(Path, searchPattern);
            }
        } catch (DirectoryNotFoundException) {
            return Enumerable.Empty<string>();
        }
    }

    public RepositoryPackage GetVersionPackage(string version, bool mustExist) {
        return GetVersionPackage(new PackageVersion(version), mustExist);
    }

    public RepositoryPackage GetVersionPackage(PackageVersion version, bool mustExist) {
        if (version.ToString() == TemplatedRepositoryPackage.TemplateDirName) {
            // disallow creating this version, otherwise we couldn't distinguish between a templated and direct package types
            throw new InvalidPackageVersionException(
                    $"Version of a package in the repository must not be '{TemplatedRepositoryPackage.TemplateDirName}'.");
        }

        var package = GetPackage(version);
        if (mustExist && !package.Exists) {
            // FIXME: bullshit path for TemplatedRepositoryPackage
            throw new RepositoryPackageVersionNotFoundException(
                    $"Package '{PackageName}' in the repository does not have version '{version}', expected path: {package.Path}");
        }
        return package;
    }

    public RepositoryPackage? GetLatestPackage() {
        var latestVersion = EnumerateVersionStrings().Select(v => new PackageVersion(v)).Max();
        return latestVersion == null ? null : GetPackage(latestVersion);
    }

    private RepositoryPackage GetPackage(PackageVersion version) {
        return RepositoryPackage.Create(this, version);
    }
}

[PublicAPI]
public abstract class RepositoryPackage : Package {
    public readonly PackageVersion Version;
    [Hidden] public readonly RepositoryVersionedPackage Container;

    protected RepositoryPackage(RepositoryVersionedPackage parent, PackageVersion version, string packagePath)
            : base(parent.PackageName, packagePath) {
        Version = version;
        Container = parent;
    }

    public static RepositoryPackage Create(RepositoryVersionedPackage parent, PackageVersion version) {
        return parent.IsTemplated
                ? new TemplatedRepositoryPackage(parent, version)
                : new DirectRepositoryPackage(parent, version);
    }

    public abstract void ImportTo(ImportedPackage target);
}

[PublicAPI]
public sealed class DirectRepositoryPackage : RepositoryPackage {
    public DirectRepositoryPackage(RepositoryVersionedPackage parent, PackageVersion version)
            : base(parent, version, IOPath.Combine(parent.Path, version.ToString())) {}

    public override void ImportTo(ImportedPackage target) {
        // copy the resource directory
        var resDir = new DirectoryInfo(ManifestResourceDirPath);
        if (resDir.Exists) {
            FileUtils.CopyDirectory(resDir, target.ManifestResourceDirPath);
        }
        // copy the manifest
        File.Copy(ManifestPath, target.ManifestPath);
    }
}

[PublicAPI]
public sealed class TemplatedRepositoryPackage : RepositoryPackage {
    internal const string TemplateDirName = ".template";

    [Hidden] public string TemplatePath => base.GetManifestPath(); // a bit too hacky?
    [Hidden] public override bool Exists => base.Exists && File.Exists(ManifestPath);

    public TemplatedRepositoryPackage(RepositoryVersionedPackage parent, PackageVersion version)
            : base(parent, version, IOPath.Combine(parent.Path, TemplateDirName)) {}

    public override void ImportTo(ImportedPackage target) {
        // copy the resource directory
        var resDir = new DirectoryInfo(ManifestResourceDirPath);
        if (resDir.Exists) {
            FileUtils.CopyDirectory(resDir, target.ManifestResourceDirPath);
        }
        // write the manifest
        // TODO: figure out how to avoid calling .Substitute twice when first validating, and then importing the package
        TemplateFile.Substitute(TemplatePath, ManifestPath, target.ManifestPath);
    }

    protected override string GetManifestPath() {
        return IOPath.Combine(Container.Path, $"{Version}.psd1");
    }

    protected override PackageManifest LoadManifest() {
        return new PackageManifest(ManifestPath, TemplateFile.Substitute(TemplatePath, ManifestPath));
    }
}
