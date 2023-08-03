using System.IO;
using IOPath = System.IO.Path;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

[PublicAPI]
public abstract class Package {
    public readonly string PackageName;
    [Hidden] public readonly string Path;
    [Hidden] public string ManifestPath => _manifest != null ? Manifest.Path : GetManifestPath();

    [Hidden] public virtual bool Exists => Directory.Exists(Path);

    // TODO: make this public?
    internal string ManifestResourceDirPath => IOPath.Combine(Path, PathConfig.PackagePaths.ManifestResourceRelPath);

    private PackageManifest? _manifest;
    [Hidden]
    public PackageManifest Manifest {
        get {
            if (_manifest == null) {
                ReloadManifest();
            }
            return _manifest!;
        }
    }

    protected Package(string packageName, string packagePath, PackageManifest? manifest = null) {
        Verify.Assert.PackageName(packageName);
        Verify.Assert.FilePath(packagePath);
        PackageName = packageName;
        Path = packagePath;
        _manifest = manifest;
    }

    /// <exception cref="DirectoryNotFoundException">Thrown if the package directory does not exist.</exception>
    /// <exception cref="PackageManifestNotFoundException">Thrown if the package manifest file does not exist.</exception>
    /// <exception cref="PackageManifestParseException">Thrown if the package manifest file is not a valid PowerShell data file (.psd1).</exception>
    public void ReloadManifest() {
        if (!Exists) {
            throw new DirectoryNotFoundException($"Tried to read package manifest of a non-existent package at '{Path}'.");
        }
        _manifest = LoadManifest();
    }

    protected virtual string GetManifestPath() {
        return IOPath.Combine(Path, PathConfig.PackagePaths.ManifestRelPath);
    }

    protected virtual PackageManifest LoadManifest() {
        return new PackageManifest(ManifestPath);
    }
}
