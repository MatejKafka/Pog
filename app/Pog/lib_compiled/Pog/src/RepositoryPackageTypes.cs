using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using IOPath = System.IO.Path;

namespace Pog;

public class RepositoryPackageNotFoundException(string message) : PackageNotFoundException(message);

public class RepositoryPackageVersionNotFoundException(string message) : PackageNotFoundException(message);

[PublicAPI]
public class Repository {
    public readonly string Path;

    public Repository(string manifestRepositoryDirPath) {
        Path = manifestRepositoryDirPath;
    }

    public IEnumerable<string> EnumeratePackageNames(string searchPattern = "*") {
        try {
            return FsUtils.EnumerateNonHiddenDirectoryNames(Path, searchPattern);
        } catch (DirectoryNotFoundException) {
            return Enumerable.Empty<string>();
        }
    }

    public IEnumerable<RepositoryVersionedPackage> Enumerate(string searchPattern = "*") {
        var repo = this;
        return EnumeratePackageNames(searchPattern).Select(p => new RepositoryVersionedPackage(repo, p));
    }

    public RepositoryVersionedPackage GetPackage(string packageName, bool resolveName, bool mustExist) {
        Verify.PackageName(packageName);
        if (resolveName) {
            packageName = FsUtils.GetResolvedChildName(Path, packageName);
        }
        var package = new RepositoryVersionedPackage(this, packageName);
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
    public readonly Repository Repository;
    public readonly string Path;
    public bool Exists => Directory.Exists(Path);
    public bool IsTemplated => Directory.Exists(TemplateDirPath);

    public string TemplateDirPath => IOPath.Combine(Path, PathConfig.PackagePaths.RepositoryTemplateDirName);
    internal string TemplatePath => IOPath.Combine(TemplateDirPath, PathConfig.PackagePaths.ManifestRelPath);

    internal RepositoryVersionedPackage(Repository repository, string packageName) {
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
                return FsUtils.EnumerateNonHiddenFileNames(Path, searchPattern + ".psd1")
                        .Select(IOPath.GetFileNameWithoutExtension);
            } else {
                return FsUtils.EnumerateNonHiddenDirectoryNames(Path, searchPattern);
            }
        } catch (DirectoryNotFoundException) {
            return Enumerable.Empty<string>();
        }
    }

    public RepositoryPackage GetVersionPackage(string version, bool mustExist) {
        return GetVersionPackage(new PackageVersion(version), mustExist);
    }

    public RepositoryPackage GetVersionPackage(PackageVersion version, bool mustExist) {
        if (version.ToString() == PathConfig.PackagePaths.RepositoryTemplateDirName) {
            // disallow creating this version, otherwise we couldn't distinguish between a templated and direct package types
            throw new InvalidPackageVersionException(
                    $"Version of a package in the repository must not be '{PathConfig.PackagePaths.RepositoryTemplateDirName}'.");
        }

        var package = GetPackage(version);
        if (mustExist && !package.Exists) {
            var path = IsTemplated ? package.ManifestPath : package.Path;
            throw new RepositoryPackageVersionNotFoundException(
                    $"Package '{PackageName}' in the repository does not have version '{version}', expected path: {path}");
        }
        return package;
    }

    public RepositoryPackage GetLatestPackage() {
        var latestVersion = EnumerateVersionStrings().Select(v => new PackageVersion(v)).Max();
        if (latestVersion != null) {
            return GetPackage(latestVersion);
        } else {
            throw new RepositoryPackageVersionNotFoundException(
                    $"Package '{PackageName}' in the repository does not have any versions, expected path: {Path}");
        }
    }

    private RepositoryPackage GetPackage(PackageVersion version) {
        return RepositoryPackage.Create(this, version);
    }
}

[PublicAPI]
public abstract class RepositoryPackage : Package {
    public readonly PackageVersion Version;
    public readonly RepositoryVersionedPackage Container;

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

    public string GetDescriptionString() {
        return $"package '{PackageName}', version '{Version}'";
    }

    public void ImportTo(ImportedPackage target) {
        // ensure target directory exists
        Directory.CreateDirectory(target.Path);
        // remove any previous manifest
        target.RemoveManifest();

        // copy the resource directory
        var resDir = new DirectoryInfo(ManifestResourceDirPath);
        if (resDir.Exists) {
            FsUtils.CopyDirectory(resDir, target.ManifestResourceDirPath);
        }

        // write the manifest
        ImportManifestTo(target);

        Debug.Assert(target.MatchesRepositoryManifest(this));
    }

    protected abstract void ImportManifestTo(ImportedPackage target);
}

[PublicAPI]
public sealed class DirectRepositoryPackage : RepositoryPackage {
    public DirectRepositoryPackage(RepositoryVersionedPackage parent, PackageVersion version)
            : base(parent, version, IOPath.Combine(parent.Path, version.ToString())) {}

    protected override void ImportManifestTo(ImportedPackage target) {
        File.Copy(ManifestPath, target.ManifestPath);
    }

    protected override PackageManifest LoadManifest() {
        return new PackageManifest(ManifestPath, owningPackage: this);
    }
}

[PublicAPI]
public sealed class TemplatedRepositoryPackage : RepositoryPackage {
    public string TemplatePath => base.GetManifestPath(); // a bit too hacky?
    public override bool Exists => base.Exists && File.Exists(ManifestPath);

    public TemplatedRepositoryPackage(RepositoryVersionedPackage parent, PackageVersion version)
            : base(parent, version, parent.TemplateDirPath) {}

    protected override void ImportManifestTo(ImportedPackage target) {
        // TODO: figure out how to avoid calling .Substitute twice when first validating, and then importing the package
        ManifestTemplateFile.Substitute(TemplatePath, ManifestPath, target.ManifestPath);
    }

    protected override string GetManifestPath() {
        return IOPath.Combine(Container.Path, $"{Version}.psd1");
    }

    protected override PackageManifest LoadManifest() {
        return new PackageManifest(ManifestPath, GetManifestString(), owningPackage: this);
    }

    internal string GetManifestString() {
        return ManifestTemplateFile.Substitute(TemplatePath, ManifestPath);
    }
}
