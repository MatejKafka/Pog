using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public IEnumerable<string> EnumerateGeneratedPackageNames(string searchPattern = "*") {
        return EnumerateGeneratedPackages(searchPattern).Select(p => p.PackageName);
    }

    public IEnumerable<LocalRepositoryVersionedPackage> EnumerateGeneratedPackages(string searchPattern = "*") {
        return Enumerate(searchPattern).Cast<LocalRepositoryVersionedPackage>().Where(p => p.HasGenerator);
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
    public bool HasGenerator => File.Exists(GeneratorPath);

    public string TemplateDirPath => $"{Path}\\{PPaths.RepositoryTemplateDirName}";
    public string TemplatePath => $"{TemplateDirPath}\\{PPaths.ManifestFileName}";
    public string GeneratorPath => $"{TemplateDirPath}\\{PPaths.GeneratorFileName}";

    private PackageGeneratorManifest? _generator;
    public PackageGeneratorManifest Generator => _generator ?? ReloadGenerator();

    internal override string ExpectedPathStr => $"expected path: {Path}";

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
            return [];
        }
    }

    public override RepositoryPackage GetVersionPackage(PackageVersion version, bool mustExist) {
        if (version.ToString() == PPaths.RepositoryTemplateDirName) {
            // disallow creating this version, otherwise we couldn't distinguish between a templated and direct package types
            throw new InvalidPackageVersionException(
                    $"Version of a package in the repository must not be '{PPaths.RepositoryTemplateDirName}'.");
        }
        return base.GetVersionPackage(version, mustExist);
    }

    protected override RepositoryPackage GetPackageUnchecked(PackageVersion version) {
        return this.IsTemplated
                ? new TemplatedLocalRepositoryPackage(this, version)
                : new DirectLocalRepositoryPackage(this, version);
    }

    /// <exception cref="PackageManifestNotFoundException">Thrown if the package generator does not exist.</exception>
    /// <exception cref="PackageManifestParseException">Thrown if the package generator file is not a valid PowerShell data file (.psd1).</exception>
    public PackageGeneratorManifest ReloadGenerator() {
        return _generator = new PackageGeneratorManifest(GeneratorPath);
    }
}

/// Specific version of a package contained in a local Pog package repository.
[PublicAPI]
public abstract class LocalRepositoryPackage(LocalRepositoryVersionedPackage parent, PackageVersion version, string path)
        : RepositoryPackage(parent, version), ILocalPackage {
    public string Path {get; init;} = path;
    public abstract string ManifestPath {get;}
    internal override string ExpectedPathStr => $"expected path: {Path}";
}

[PublicAPI]
public sealed class DirectLocalRepositoryPackage(LocalRepositoryVersionedPackage parent, PackageVersion version)
        : LocalRepositoryPackage(parent, version, $"{parent.Path}\\{version}") {
    public override string ManifestPath => $"{Path}\\{PPaths.ManifestFileName}";
    public override bool Exists => Directory.Exists(Path);

    protected override PackageManifest LoadManifest() {
        if (!Exists) {
            throw new PackageNotFoundException($"Cannot read the package manifest of a non-existent package: {Path}");
        }
        return new PackageManifest(ManifestPath, owningPackage: this);
    }
}

[PublicAPI]
public sealed class TemplatedLocalRepositoryPackage(LocalRepositoryVersionedPackage parent, PackageVersion version)
        : LocalRepositoryPackage(parent, version, $"{parent.Path}\\{version}.psd1") {
    public override string ManifestPath => Path;
    public override bool Exists => File.Exists(Path) && Directory.Exists(TemplateDirPath);

    public string TemplateDirPath => ((LocalRepositoryVersionedPackage) Container).TemplateDirPath;
    public string TemplatePath => ((LocalRepositoryVersionedPackage) Container).TemplatePath;

    protected override PackageManifest LoadManifest() {
        if (!Exists) {
            throw new PackageNotFoundException($"Cannot read the package manifest of a non-existent package at '{Path}'.");
        }

        var manifestStr = ManifestTemplateFile.Substitute(TemplatePath, ManifestPath);
        return new PackageManifest(manifestStr, ManifestPath, owningPackage: this);
    }
}
