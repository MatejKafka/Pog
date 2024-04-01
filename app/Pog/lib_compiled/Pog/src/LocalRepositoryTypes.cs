using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Pog.Utils;
using IOPath = System.IO.Path;
using PPaths = Pog.PathConfig.PackagePaths;

namespace Pog;

[PublicAPI]
public sealed class LocalRepository(string manifestRepositoryDirPath) : IRepository {
    public readonly string Path = manifestRepositoryDirPath;
    public bool Exists => Directory.Exists(Path);

    public IEnumerable<string> EnumeratePackageNames(string searchPattern = "*") {
        try {
            return FsUtils.EnumerateNonHiddenDirectoryNames(Path, searchPattern);
        } catch (DirectoryNotFoundException) {
            throw new RepositoryNotFoundException($"Package repository does not seem to exist: {Path}");
        }
    }

    public IEnumerable<RepositoryVersionedPackage> Enumerate(string searchPattern = "*") {
        var repo = this;
        return EnumeratePackageNames(searchPattern).Select(p => new LocalRepositoryVersionedPackage(repo, p));
    }

    public RepositoryVersionedPackage GetPackage(string packageName, bool resolveName, bool mustExist) {
        Verify.PackageName(packageName);
        if (resolveName) {
            packageName = FsUtils.GetResolvedChildName(Path, packageName);
        }
        var package = new LocalRepositoryVersionedPackage(this, packageName);
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
public sealed class LocalRepositoryVersionedPackage : RepositoryVersionedPackage {
    public override IRepository Repository {get;}
    public readonly string Path;
    public override bool Exists => Directory.Exists(Path);
    public bool IsTemplated => Directory.Exists(TemplateDirPath);

    public string TemplateDirPath => $"{Path}\\{PPaths.RepositoryTemplateDirName}";
    internal string TemplatePath => $"{TemplateDirPath}\\{PPaths.ManifestRelPath}";
    protected override string ExpectedPathStr => $"expected path: {Path}";

    internal LocalRepositoryVersionedPackage(LocalRepository repository, string packageName) : base(packageName) {
        Repository = repository;
        Path = $"{repository.Path}\\{packageName}";
    }

    public override IEnumerable<string> EnumerateVersionStrings(string searchPattern = "*") {
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

    protected override RepositoryPackage GetPackageUnchecked(PackageVersion version) {
        return this.IsTemplated
                ? new TemplatedLocalRepositoryPackage(this, version)
                : new DirectLocalRepositoryPackage(this, version);
    }
}

[PublicAPI]
public abstract class LocalRepositoryPackage(LocalRepositoryVersionedPackage parent, PackageVersion version, string path)
        : RepositoryPackage(parent, version), ILocalPackage {
    public string Path {get; init;} = path;
    public abstract string ManifestPath {get;}
    protected abstract string ManifestResourceDirPath {get;}
    protected abstract void ImportManifestTo(string targetManifestPath);

    public override void ImportTo(ImportedPackage target) {
        // remove any previous manifest
        target.RemoveManifest();
        // ensure target directory exists
        Directory.CreateDirectory(target.Path);

        // copy the resource directory
        var resDir = new DirectoryInfo(ManifestResourceDirPath);
        if (resDir.Exists) {
            FsUtils.CopyDirectory(resDir, target.ManifestResourceDirPath);
        }

        // write the manifest
        ImportManifestTo(target.ManifestPath);

        Debug.Assert(MatchesImportedManifest(target));
    }

    public override bool MatchesImportedManifest(ImportedPackage p) {
        // compare resource dirs
        if (!FsUtils.DirectoryTreeEqual(ManifestResourceDirPath, p.ManifestResourceDirPath)) {
            return false;
        }

        // compare manifest
        var importedManifest = new FileInfo(p.ManifestPath);
        if (!importedManifest.Exists) {
            return false;
        } else if (this is DirectLocalRepositoryPackage dp) {
            return importedManifest.Exists && FsUtils.FileContentEqual(new FileInfo(dp.ManifestPath), importedManifest);
        } else if (this is TemplatedLocalRepositoryPackage tp) {
            var repoManifest = Encoding.UTF8.GetBytes(tp.GetManifestString());
            return FsUtils.FileContentEqual(repoManifest, importedManifest);
        } else {
            throw new UnreachableException();
        }
    }
}

[PublicAPI]
public sealed class DirectLocalRepositoryPackage(LocalRepositoryVersionedPackage parent, PackageVersion version)
        : LocalRepositoryPackage(parent, version, $"{parent.Path}\\{version}") {
    public override string ManifestPath => $"{Path}\\{PPaths.ManifestRelPath}";
    protected override string ManifestResourceDirPath => $"{Path}\\{PPaths.ManifestResourceRelPath}";
    public override bool Exists => Directory.Exists(Path);

    protected override void ImportManifestTo(string targetManifestPath) {
        File.Copy(ManifestPath, targetManifestPath);
    }

    protected override PackageManifest LoadManifest() {
        if (!Exists) {
            throw new PackageNotFoundException($"Tried to read the package manifest of a non-existent package at '{Path}'.");
        }
        return new PackageManifest(ManifestPath, owningPackage: this);
    }
}

[PublicAPI]
public sealed class TemplatedLocalRepositoryPackage(LocalRepositoryVersionedPackage parent, PackageVersion version)
        : LocalRepositoryPackage(parent, version, $"{parent.Path}\\{version}.psd1") {
    public override string ManifestPath => Path;
    protected override string ManifestResourceDirPath => $"{TemplateDirPath}\\{PPaths.ManifestResourceRelPath}";
    public override bool Exists => File.Exists(Path) && Directory.Exists(TemplateDirPath);

    private string TemplateDirPath => ((LocalRepositoryVersionedPackage) Container).TemplateDirPath;
    private string TemplatePath => $"{TemplateDirPath}\\{PPaths.ManifestRelPath}";

    protected override void ImportManifestTo(string targetManifestPath) {
        // TODO: figure out how to avoid calling .Substitute twice when first validating, and then importing the package
        ManifestTemplateFile.Substitute(TemplatePath, ManifestPath, targetManifestPath);
    }

    protected override PackageManifest LoadManifest() {
        if (!Exists) {
            throw new PackageNotFoundException($"Tried to read the package manifest of a non-existent package at '{Path}'.");
        }
        return new PackageManifest(GetManifestString(), ManifestPath, owningPackage: this);
    }

    internal string GetManifestString() {
        return ManifestTemplateFile.Substitute(TemplatePath, ManifestPath);
    }
}
