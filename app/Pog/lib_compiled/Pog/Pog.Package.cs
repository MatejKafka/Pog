using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

[PublicAPI]
public class Package {
    public string PackageName {get;}
    [Hidden] public string Path {get;}
    [Hidden] public string ManifestPath {get;}

    [Hidden] public bool Exists => Directory.Exists(this.Path);
    [Hidden] public bool ManifestExists => File.Exists(this.ManifestPath);

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

    internal Package(string packageName, string packagePath, PackageManifest? manifest = null) {
        Verify.Assert.PackageName(packageName);
        PackageName = packageName;
        Path = packagePath;
        ManifestPath = System.IO.Path.Combine(Path, PathConfig.PackageManifestRelPath);
        if (manifest != null) {
            _manifest = manifest;
        }
    }

    /// <exception cref="DirectoryNotFoundException">Thrown if the package directory does not exist.</exception>
    /// <exception cref="PackageManifestNotFoundException">Thrown if the package manifest file does not exist.</exception>
    /// <exception cref="PackageManifestParseException">Thrown if the package manifest file is not a valid PowerShell data file (.psd1).</exception>
    public void ReloadManifest() {
        if (!Exists) {
            throw new InternalError($"Tried to read package manifest of a non-existent package at '{Path}'.");
        }
        _manifest = new PackageManifest(ManifestPath);
    }
}